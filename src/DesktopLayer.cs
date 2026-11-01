using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DesktopTodo
{
    // Keep WPF's layered HWND top-level. Only this HWND is repositioned; shell
    // windows are observed, never reparented, restyled, messaged or reordered.
    public sealed class DesktopLayer : IDisposable
    {
        private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        private const long WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
        private const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4;
        private const uint SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_NOOWNERZORDER = 0x200;
        private const uint ZFlags = SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOOWNERZORDER;
        private const uint GW_HWNDPREV = 3, GW_HWNDNEXT = 2, GA_ROOT = 2;
        private const int WM_WINDOWPOSCHANGING = 0x46, WM_WINDOWPOSCHANGED = 0x47;
        private const int WM_SHOWWINDOW = 0x18, WM_ACTIVATE = 6, WM_SYSCOMMAND = 0x112;
        private const uint EVENT_SYSTEM_FOREGROUND = 3, EVENT_OBJECT_REORDER = 0x8004;
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        private readonly Window window;
        private readonly DispatcherTimer retry;
        private readonly Native.WinEventCallback eventCallback;
        private readonly HwndSourceHook messageHook;
        private HwndSource source;
        private IntPtr hwnd, desktop, desktopView, foregroundHook, reorderHook;
        private bool pinned, changing, queued, disposed;
        private int screenX, screenY;

        public bool IsAttached { get; private set; }
        public string Status { get; private set; }

        public DesktopLayer(Window window)
        {
            if (window == null) throw new ArgumentNullException("window");
            this.window = window;
            pinned = window.Topmost;
            window.ShowActivated = pinned;
            Status = "等待桌面层级跟随。";
            eventCallback = ObserveWindowEvent;
            messageHook = ObserveOwnMessage;
            window.SourceInitialized += SourceInitialized;
            window.Loaded += WindowLoaded;
            window.Closed += WindowClosed;
            retry = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher);
            retry.Interval = TimeSpan.FromSeconds(1);
            retry.Tick += Retry;
            // Out-of-context callbacks are delivered on this UI thread. Retain
            // both delegates until UnhookWinEvent/RemoveHook have completed.
            foregroundHook = Native.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, eventCallback, 0, 0, 0);
            reorderHook = Native.SetWinEventHook(EVENT_OBJECT_REORDER, EVENT_OBJECT_REORDER,
                IntPtr.Zero, eventCallback, 0, 0, 0);
            InstallOwnHook();
            retry.Start();
        }

        private void SourceInitialized(object sender, EventArgs args)
        {
            InstallOwnHook();
            QueueEnsure();
        }

        private void WindowLoaded(object sender, RoutedEventArgs args) { QueueEnsure(); }
        private void Retry(object sender, EventArgs args) { EnsureAttached(); }
        private void WindowClosed(object sender, EventArgs args) { Dispose(); }

        private bool InstallOwnHook()
        {
            if (disposed) return false;
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || !Native.IsWindow(handle)) return false;
            if (hwnd == handle && source != null) return true;
            if (source != null) source.RemoveHook(messageHook);
            hwnd = handle;
            source = HwndSource.FromHwnd(hwnd);
            if (source == null) return false;
            source.AddHook(messageHook);
            return true;
        }

        public void SetPinned(bool pinned)
        {
            if (disposed) return;
            this.pinned = pinned;
            changing = true;
            try
            {
                window.ShowActivated = pinned;
                window.Topmost = pinned;
            }
            finally { changing = false; }
            IsAttached = false;
            EnsureAttached();
        }

        public void Position(int screenX, int screenY)
        {
            if (disposed) return;
            this.screenX = screenX;
            this.screenY = screenY;
            if (!InstallOwnHook()) return;
            bool previousChanging = changing;
            changing = true;
            try
            {
                // Input is already in physical screen pixels. Do not change DPI
                // awareness or transform into Explorer's client coordinates.
                Check(Native.SetWindowPos(hwnd, IntPtr.Zero, screenX, screenY, 0, 0,
                    SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER));
            }
            catch (Exception ex) { Status = "桌面定位暂未完成：" + ex.Message; }
            finally { changing = previousChanging; }
            QueueEnsure();
        }

        public void EnsureAttached()
        {
            if (disposed || changing) return;
            if (!InstallOwnHook())
            {
                IsAttached = false;
                Status = "等待待办窗口创建。";
                return;
            }
            if (!window.IsVisible || !Native.IsWindowVisible(hwnd))
            {
                IsAttached = false;
                Status = "待办已隐藏，显示后恢复桌面层级。";
                return;
            }

            changing = true;
            try
            {
                long exStyle = Native.ReadStyle(hwnd, GWL_EXSTYLE);
                long toolStyle = (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
                if (toolStyle != exStyle)
                {
                    Native.WriteStyle(hwnd, GWL_EXSTYLE, toolStyle);
                    Check(Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                        ZFlags | SWP_NOZORDER | SWP_FRAMECHANGED));
                }
                if (pinned)
                {
                    if ((toolStyle & WS_EX_TOPMOST) == 0) SetZOrder(HWND_TOPMOST);
                    IsAttached = false;
                    Status = "始终置顶模式。";
                    return;
                }

                // Demote before selecting an insertion neighbour. Inserting
                // after a topmost HWND would otherwise promote this window.
                if ((toolStyle & WS_EX_TOPMOST) != 0) SetZOrder(HWND_NOTOPMOST);
                desktop = FindDesktopHost(out desktopView);
                if (desktop == IntPtr.Zero)
                {
                    if (Native.GetWindow(hwnd, GW_HWNDNEXT) != IntPtr.Zero) SetZOrder(HWND_BOTTOM);
                    IsAttached = false;
                    Status = "桌面宿主暂不可用，已置底并等待自动恢复。";
                    return;
                }

                // The actual icon-host top-level HWND moves when Show Desktop
                // raises the desktop. Follow immediately above it. Ordinarily
                // this is below application windows; when desktop is raised it
                // is the front of the non-topmost band. Do not move Explorer.
                IntPtr preceding = Native.GetWindow(desktop, GW_HWNDPREV);
                if (preceding == hwnd)
                {
                    IsAttached = true;
                }
                else
                {
                    IntPtr after = preceding;
                    if (after == IntPtr.Zero || IsTopmost(after)) after = HWND_TOP;
                    SetZOrder(after);
                    // The shell can reorder concurrently. Do not report success
                    // until native ordering actually puts us above the desktop.
                    IsAttached = IsAbove(hwnd, desktop) && !IsTopmost(hwnd);
                }
                Status = IsAttached ? "已跟随桌面层级，其他应用可遮挡。" : "桌面层级正在变化，将自动重试。";
                if (IsAttached && (foregroundHook == IntPtr.Zero || reorderHook == IntPtr.Zero))
                    Status += "（使用每秒自动校正）";
            }
            catch (Exception ex)
            {
                IsAttached = false;
                Status = "桌面层级跟随暂未恢复：" + ex.Message;
            }
            finally { changing = false; }
        }

        private void SetZOrder(IntPtr after)
        {
            Check(Native.SetWindowPos(hwnd, after, 0, 0, 0, 0, ZFlags));
        }

        private static void Check(bool result)
        {
            if (!result) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        private static bool IsTopmost(IntPtr handle)
        {
            return handle != IntPtr.Zero && (Native.ReadStyle(handle, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
        }

        private static bool IsAbove(IntPtr first, IntPtr second)
        {
            // Bound the walk: desktop/window handles may disappear mid-query.
            IntPtr current = Native.GetWindow(second, GW_HWNDPREV);
            for (int i = 0; current != IntPtr.Zero && i < 4096; i++)
            {
                if (current == first) return true;
                current = Native.GetWindow(current, GW_HWNDPREV);
            }
            return false;
        }

        private IntPtr ObserveOwnMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (disposed) return IntPtr.Zero;
            // No minimize affordance exists on a desktop widget. In particular,
            // do not disappear when Show Desktop broadcasts minimize commands.
            if (message == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0L) == 0xF020)
            {
                handled = true;
                QueueEnsure();
                return IntPtr.Zero;
            }
            if (!pinned && !changing && message == WM_WINDOWPOSCHANGING && lParam != IntPtr.Zero)
            {
                WindowPosition position = (WindowPosition)Marshal.PtrToStructure(lParam, typeof(WindowPosition));
                if ((position.Flags & SWP_NOZORDER) == 0)
                {
                    // Allow ordinary mouse/keyboard focus and all geometry and
                    // visibility updates, but activation cannot raise the widget.
                    position.Flags |= SWP_NOZORDER;
                    Marshal.StructureToPtr(position, lParam, false);
                    QueueEnsure();
                }
            }
            if (!changing && (message == WM_WINDOWPOSCHANGED || message == WM_SHOWWINDOW || message == WM_ACTIVATE))
                QueueEnsure();
            return IntPtr.Zero;
        }

        private void ObserveWindowEvent(IntPtr hook, uint eventId, IntPtr eventWindow,
            int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (disposed || pinned) return;
            if (eventId == EVENT_SYSTEM_FOREGROUND)
            {
                QueueEnsure();
                return;
            }
            if (eventId != EVENT_OBJECT_REORDER || eventWindow == hwnd) return;
            // Desktop reordering can be reported on its root, icon view or the
            // system desktop window; unrelated controls generate many events.
            if (eventWindow == IntPtr.Zero || eventWindow == desktop || eventWindow == desktopView ||
                eventWindow == Native.GetDesktopWindow() || eventWindow == Native.GetShellWindow() ||
                (desktop != IntPtr.Zero && Native.GetAncestor(eventWindow, GA_ROOT) == desktop))
                QueueEnsure();
        }

        private void QueueEnsure()
        {
            if (disposed || queued || window.Dispatcher.HasShutdownStarted) return;
            queued = true;
            window.Dispatcher.BeginInvoke(new Action(delegate
            {
                queued = false;
                if (!disposed) EnsureAttached();
            }), DispatcherPriority.Background);
        }

        private static IntPtr FindDesktopHost(out IntPtr view)
        {
            view = IntPtr.Zero;
            IntPtr shell = Native.GetShellWindow();
            if (shell == IntPtr.Zero || !Native.IsWindow(shell)) return IntPtr.Zero;
            uint shellPid;
            Native.GetWindowThreadProcessId(shell, out shellPid);
            if (shellPid == 0) return IntPtr.Zero;
            var roots = new List<IntPtr>();
            roots.Add(shell);
            Native.EnumWindows(delegate(IntPtr candidate, IntPtr ignored)
            {
                uint pid;
                Native.GetWindowThreadProcessId(candidate, out pid);
                string name = Native.ClassName(candidate);
                if (pid == shellPid && (name == "Progman" || name == "WorkerW") && !roots.Contains(candidate))
                    roots.Add(candidate);
                return true;
            }, IntPtr.Zero);
            foreach (IntPtr root in roots)
            {
                if (!Native.IsWindowVisible(root)) continue;
                IntPtr found = IntPtr.Zero;
                Native.EnumChildWindows(root, delegate(IntPtr child, IntPtr ignored)
                {
                    if (Native.ClassName(child) != "SHELLDLL_DefView") return true;
                    uint pid;
                    Native.GetWindowThreadProcessId(child, out pid);
                    if (pid == shellPid && Native.GetAncestor(child, GA_ROOT) == root)
                    {
                        found = child;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
                NativeRect bounds;
                if (found != IntPtr.Zero && Native.GetWindowRect(root, out bounds) &&
                    bounds.Right > bounds.Left && bounds.Bottom > bounds.Top)
                {
                    view = found;
                    return root;
                }
            }
            return IntPtr.Zero;
        }

        public string Describe()
        {
            var text = new StringBuilder(Status);
            text.Append("; attached=").Append(IsAttached).Append(" pinned=").Append(pinned);
            text.Append(" desired=").Append(screenX).Append(',').Append(screenY);
            text.Append("; foreground=").Append(Native.GetForegroundWindow());
            text.Append("; hooks=").Append(foregroundHook != IntPtr.Zero).Append(',').Append(reorderHook != IntPtr.Zero);
            foreach (IntPtr handle in new IntPtr[] { hwnd, desktop, desktopView })
            {
                text.Append("; ").Append(Native.ClassName(handle)).Append(" h=").Append(handle);
                if (handle == IntPtr.Zero || !Native.IsWindow(handle)) continue;
                NativeRect bounds;
                Native.GetWindowRect(handle, out bounds);
                text.Append(" parent=").Append(Native.GetParent(handle)).Append(" visible=").Append(Native.IsWindowVisible(handle));
                text.Append(" prev=").Append(Native.GetWindow(handle, GW_HWNDPREV));
                text.Append(" next=").Append(Native.GetWindow(handle, GW_HWNDNEXT));
                text.Append(" rect=").Append(bounds.Left).Append(',').Append(bounds.Top).Append(',').Append(bounds.Right).Append(',').Append(bounds.Bottom);
                try
                {
                    text.Append(" style=").Append(Native.ReadStyle(handle, GWL_STYLE).ToString("X"));
                    text.Append(" ex=").Append(Native.ReadStyle(handle, GWL_EXSTYLE).ToString("X"));
                }
                catch (Win32Exception) { text.Append(" (window changed)"); }
            }
            return text.ToString();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            retry.Stop();
            retry.Tick -= Retry;
            window.SourceInitialized -= SourceInitialized;
            window.Loaded -= WindowLoaded;
            window.Closed -= WindowClosed;
            if (foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(foregroundHook);
            if (reorderHook != IntPtr.Zero) Native.UnhookWinEvent(reorderHook);
            foregroundHook = reorderHook = IntPtr.Zero;
            if (source != null) source.RemoveHook(messageHook);
            source = null;
            IsAttached = false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPosition
        {
            public IntPtr Window, InsertAfter;
            public int X, Y, Width, Height;
            public uint Flags;
        }

        private static class Native
        {
            public delegate bool EnumWindowCallback(IntPtr hwnd, IntPtr parameter);
            public delegate void WinEventCallback(IntPtr hook, uint eventId, IntPtr hwnd,
                int objectId, int childId, uint eventThread, uint eventTime);
            [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
            [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
            [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
            [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
            [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
            [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowCallback callback, IntPtr parameter);
            [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int size);
            [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
            [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
            [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventCallback callback, uint processId, uint threadId, uint flags);
            [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
            [DllImport("kernel32.dll", EntryPoint = "SetLastError")] private static extern void SetLastErrorNative(uint error);
            [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
            [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong32(IntPtr hwnd, int index);
            [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
            [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

            public static string ClassName(IntPtr hwnd)
            {
                if (hwnd == IntPtr.Zero) return "none";
                var text = new StringBuilder(128);
                GetClassName(hwnd, text, text.Capacity);
                return text.ToString();
            }

            public static long ReadStyle(IntPtr hwnd, int index)
            {
                SetLastErrorNative(0);
                long value = IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index).ToInt64() : GetWindowLong32(hwnd, index);
                int error = Marshal.GetLastWin32Error();
                if (value == 0 && error != 0) throw new Win32Exception(error);
                return value & 0xFFFFFFFFL;
            }

            public static void WriteStyle(IntPtr hwnd, int index, long value)
            {
                SetLastErrorNative(0);
                long previous = IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, new IntPtr(value)).ToInt64()
                    : SetWindowLong32(hwnd, index, unchecked((int)value));
                int error = Marshal.GetLastWin32Error();
                if (previous == 0 && error != 0) throw new Win32Exception(error);
            }
        }
    }
}
