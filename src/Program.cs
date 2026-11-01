using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("桌面待办")]
[assembly: AssemblyDescription("简约透明的本地桌面待办")]
[assembly: AssemblyProduct("DesktopTodo")]
[assembly: AssemblyVersion("1.0.3.0")]
[assembly: AssemblyFileVersion("1.0.3.0")]

namespace DesktopTodo
{
    internal static class Program
    {
        private static Application app;
        private static MainWindow window;
        private static TodoRepository repository;
        private static Forms.NotifyIcon tray;
        private static MenuItem pinItem;
        private static ContextMenu trayMenu;
        private static DispatcherTimer maintenance;
        private static bool exiting;
        private static bool savedStartup;
        private static string directory;
        private static string lastMaintenanceError;

        [STAThread]
        public static int Main(string[] args)
        {
            // Keep per-monitor DPI behavior when only the exe is downloaded.
            // WPF caches these switches, so set them before entering its code.
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
            AppContext.SetSwitch("Switch.System.Windows.DoNotUsePresentationDpiCapabilityTier2OrGreater", false);
            return Run(args);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int Run(string[] args)
        {
            directory = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--data-dir" && i + 1 < args.Length)
                    directory = Path.GetFullPath(args[++i]);
                else
                {
                    MessageBox.Show("无法识别启动参数。可用参数：--data-dir <数据目录>", "桌面待办");
                    return 2;
                }
            }
            string key;
            using (var sha = SHA256.Create())
                key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).TrimEnd('\\').ToUpperInvariant()))).Replace("-", "").Substring(0, 24);
            using (var mutex = new Mutex(false, "Local\\DesktopTodo-" + key))
            {
                bool owns;
                try { owns = mutex.WaitOne(0, false); }
                catch (AbandonedMutexException) { owns = true; }
                if (!owns)
                {
                    for (int attempt = 0; attempt < 20; attempt++)
                    {
                        try
                        {
                            using (var existing = EventWaitHandle.OpenExisting("Local\\DesktopTodo-Show-" + key)) existing.Set();
                            return 0;
                        }
                        catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(100); }
                    }
                    return 0;
                }
                try
                {
                    using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DesktopTodo-Show-" + key))
                    {
                        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
                        Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
                        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("zh-CN")));
                        app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                        Trace("Application created");
                        app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                        {
                            Log(e.Exception);
                            MessageBox.Show(window, "操作未完成：" + e.Exception.Message + "\n请重试；数据文件不会被自动重置。", "桌面待办", MessageBoxButton.OK, MessageBoxImage.Warning);
                            e.Handled = true;
                        };
                        repository = new TodoRepository(directory);
                        repository.Load(DateTimeOffset.Now);
                        Trace("Repository loaded");
                        repository.ArchiveAndPrune(DateTimeOffset.Now);
                        savedStartup = repository.State.Settings.StartWithWindows;
                        CreateMainWindow();
                        Trace("MainWindow constructed");
                        CreateTray();
                        Trace("Tray created");
                        var registeredWait = ThreadPool.RegisterWaitForSingleObject(showSignal, delegate { app.Dispatcher.BeginInvoke(new Action(ShowWindow)); }, null, Timeout.Infinite, false);
                        maintenance = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
                        maintenance.Tick += delegate { Maintain(); };
                        maintenance.Start();
                        SystemEvents.PowerModeChanged += OnPower;
                        SystemEvents.DisplaySettingsChanged += OnDisplay;
                        Trace("SystemEvents subscribed");
                        app.SessionEnding += delegate { try { repository.Save(); } catch (Exception e) { Log(e); } };
                        window.Show();
                        Trace("Window shown: " + window.DesktopStatus);
#if TRACE_STARTUP
                        var diagnosticTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                        diagnosticTimer.Tick += delegate { diagnosticTimer.Stop(); Trace(window.DesktopDiagnostics); };
                        diagnosticTimer.Start();
#endif
                        if (!string.IsNullOrEmpty(repository.StartupNotice))
                            window.Dispatcher.BeginInvoke(new Action(delegate { MessageBox.Show(window, repository.StartupNotice, "数据恢复", MessageBoxButton.OK, MessageBoxImage.Information); }));
                        int result = app.Run();
                        registeredWait.Unregister(null);
                        SystemEvents.PowerModeChanged -= OnPower;
                        SystemEvents.DisplaySettingsChanged -= OnDisplay;
                        maintenance.Stop();
                        if (tray != null) { tray.Visible = false; tray.Dispose(); }
                        return result;
                    }
                }
                catch (Exception error)
                {
                    Log(error);
                    if (tray != null) { tray.Visible = false; tray.Dispose(); }
                    MessageBox.Show("桌面待办无法启动：\n" + error.Message + "\n\n数据目录：" + directory, "桌面待办", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static void Persist()
        {
            bool requested = repository.State.Settings.StartWithWindows;
            bool changed = requested != savedStartup;
            if (changed) StartupIntegration.SetEnabled(requested, directory);
            try { repository.Save(); }
            catch
            {
                if (changed) { try { StartupIntegration.SetEnabled(savedStartup, directory); } catch { } }
                throw;
            }
            savedStartup = requested;
            SyncTray();
        }

        private static void CreateMainWindow()
        {
            window = new MainWindow(repository.State, Persist);
            app.MainWindow = window;
            window.ExitRequested += delegate { Quit(); };
            window.OpenDataRequested += delegate { OpenData(); };
            window.SettingsChanged += delegate { SyncTray(); };
            window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (!exiting) { e.Cancel = true; window.Hide(); }
            };
            // Keep the tray usable if the main window is unexpectedly closed.
            window.Closed += delegate
            {
                if (exiting) return;
                var retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                retry.Tick += delegate
                {
                    retry.Stop();
                    if (exiting) return;
                    try { CreateMainWindow(); window.Show(); }
                    catch (Exception e) { Log(e); }
                };
                retry.Start();
            };
        }

        private static void CreateTray()
        {
            tray = new Forms.NotifyIcon();
            tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location) ?? System.Drawing.SystemIcons.Application;
            tray.Text = "桌面待办 · 双击显示";
            tray.MouseUp += delegate(object sender, Forms.MouseEventArgs e)
            {
                if (e.Button == Forms.MouseButtons.Right)
                    app.Dispatcher.BeginInvoke(new Action(OpenTrayMenu));
            };
            tray.MouseDoubleClick += delegate(object sender, Forms.MouseEventArgs e)
            {
                if (e.Button == Forms.MouseButtons.Left) ShowWindow();
            };
            tray.Visible = true;
        }

        private static void OpenTrayMenu()
        {
            if (exiting) return;
            if (trayMenu != null) trayMenu.IsOpen = false;
            ContextMenu menu = MenuStyles.Create(MenuStyles.IsDark(repository.State.Settings.Theme), repository.State.Settings.UiScale);
            trayMenu = menu;
            AddTrayItem(menu, window.IsVisible ? "隐藏待办" : "显示待办", delegate
            {
                if (window.IsVisible) window.Hide(); else ShowWindow();
            });
            pinItem = AddTrayItem(menu, "始终置顶", delegate
            {
                bool before = repository.State.Settings.Topmost;
                repository.State.Settings.Topmost = !before;
                try { Persist(); window.ApplySettings(); }
                catch (Exception e) { repository.State.Settings.Topmost = before; SyncTray(); MessageBox.Show(window, "设置未保存：" + e.Message, "桌面待办"); }
            });
            pinItem.IsCheckable = true;
            SyncTray();
            AddTrayItem(menu, "外观与设置", delegate { ShowWindow(); window.ShowSettings(); });
            AddTrayItem(menu, "打开数据文件夹", OpenData);
            menu.Items.Add(new Separator());
            AddTrayItem(menu, "退出", Quit);
            menu.Closed += delegate
            {
                if (trayMenu == menu) { trayMenu = null; pinItem = null; }
            };
            new TrayMenuHost(menu).OpenAtCursor();
        }

        private static MenuItem AddTrayItem(ContextMenu menu, string title, Action action)
        {
            var item = new MenuItem { Header = title };
            item.Click += delegate
            {
                menu.IsOpen = false;
                // The transparent tray anchor closes at Normal priority first;
                // only then open an editor, launch Explorer or shut down.
                app.Dispatcher.BeginInvoke(action, DispatcherPriority.Background);
            };
            menu.Items.Add(item);
            return item;
        }

        private static void SyncTray()
        {
            if (pinItem != null) pinItem.IsChecked = repository.State.Settings.Topmost;
        }

        private static void ShowWindow()
        {
            if (window == null || exiting) return;
            window.Show();
            window.WindowState = WindowState.Normal;
            window.ApplySettings();
            if (repository.State.Settings.Topmost) window.Activate();
        }

        private static void OpenData()
        {
            try { Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true }); }
            catch (Exception e) { MessageBox.Show(window, "无法打开数据文件夹：" + e.Message, "桌面待办"); }
        }

        private static void Maintain()
        {
            foreach (Window editor in app.Windows)
                if (editor != window && editor.IsVisible) return;
            try
            {
                if (repository.ArchiveAndPrune(DateTimeOffset.Now)) window.RefreshView();
                lastMaintenanceError = null;
            }
            catch (Exception e)
            {
                Log(e);
                if (lastMaintenanceError != e.Message)
                    MessageBox.Show(window, "自动归档暂未完成，当前记录仍保留。\n" + e.Message, "桌面待办", MessageBoxButton.OK, MessageBoxImage.Warning);
                lastMaintenanceError = e.Message;
            }
        }

        private static void OnPower(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume && app != null)
                app.Dispatcher.BeginInvoke(new Action(delegate { Maintain(); window.RefreshView(); window.ApplySettings(); }));
        }

        private static void OnDisplay(object sender, EventArgs e)
        {
            if (app != null) app.Dispatcher.BeginInvoke(new Action(delegate { window.ApplySettings(); }));
        }

        private static void Quit()
        {
            if (exiting) return;
            try { repository.Save(); }
            catch (Exception e)
            {
                if (MessageBox.Show(window, "保存失败：" + e.Message + "\n仍要退出吗？未保存的更改可能丢失。", "桌面待办", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
            exiting = true;
            window.ReleaseDesktopLayer();
            app.Shutdown();
        }

        private static void Log(Exception error)
        {
            try
            {
                if (!Directory.Exists(directory)) return;
                var log = Path.Combine(directory, "error.log");
                if (File.Exists(log) && new FileInfo(log).Length > 65536) File.WriteAllText(log, "", Encoding.UTF8);
                File.AppendAllText(log, DateTimeOffset.Now.ToString("o") + " " + error + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        [Conditional("TRACE_STARTUP")]
        private static void Trace(string stage)
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "startup-trace.log"), DateTimeOffset.Now.ToString("o") + " PID=" + Process.GetCurrentProcess().Id + " session=" + Process.GetCurrentProcess().SessionId + " " + stage + Environment.NewLine);
            }
            catch { }
        }
    }

    internal static class StartupIntegration
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Value = "DesktopTodo.Local";

        public static void SetEnabled(bool enabled, string dataDirectory)
        {
            string command = "\"" + Assembly.GetExecutingAssembly().Location + "\"";
            if (!string.Equals(Path.GetFullPath(dataDirectory).TrimEnd('\\'), Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                command += " --data-dir \"" + Path.GetFullPath(dataDirectory).TrimEnd('\\') + "\"";
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (key == null) throw new IOException("无法写入当前用户的开机启动设置。");
                string existing = key.GetValue(Value) as string;
                if (existing != null && !string.Equals(existing, command, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("开机启动项已指向另一个待办程序位置；请先在原位置关闭开机启动。");
                if (enabled) key.SetValue(Value, command, RegistryValueKind.String);
                else if (existing != null) key.DeleteValue(Value, false);
            }
        }
    }
}
