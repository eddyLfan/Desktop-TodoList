using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DesktopTodo
{
    // One menu surface for the widget, nested menus and the notification area.
    internal static class MenuStyles
    {
        public static ContextMenu Create(bool dark, double scale = 1)
        {
            var resources = (ResourceDictionary)XamlReader.Parse(Templates);
            resources["MenuSurface"] = Brush(dark ? "#DC222A25" : "#DCF8FBF7");
            resources["MenuInk"] = Brush(dark ? "#EEF3EF" : "#29382E");
            resources["MenuMuted"] = Brush(dark ? "#B6C5BB" : "#68786D");
            resources["MenuDisabled"] = Brush(dark ? "#718077" : "#98A49B");
            resources["MenuHover"] = Brush(dark ? "#286E9A7A" : "#184F775A");
            resources["MenuLine"] = Brush(dark ? "#2BBACABB" : "#24475F4D");
            resources["MenuMaxHeight"] = 520.0;
            var menu = new ContextMenu { Resources = resources, StaysOpen = false };
            menu.Style = (Style)resources[typeof(ContextMenu)];
            UiScaling.SetScale(menu, scale);
            menu.LayoutTransform = new ScaleTransform(UiScaling.GetScale(menu), UiScaling.GetScale(menu));
            // ContextMenu has a separate visual root. Its nested PART_Popup
            // instances already carry the ancestor transform into their own
            // PopupRoot, so applying another scale to submenus would double it.
            menu.Opened += delegate { FitToMonitor(menu); };
            return menu;
        }

        private static void FitToMonitor(ContextMenu menu)
        {
            var source = PresentationSource.FromVisual(menu) as HwndSource;
            if (source == null || source.CompositionTarget == null) return;
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(MonitorFromWindow(source.Handle, 2), ref info)) return;
            Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
            double width = (info.Work.Right - info.Work.Left) * Math.Abs(fromDevice.M11);
            double height = (info.Work.Bottom - info.Work.Top) * Math.Abs(fromDevice.M22);
            double effectiveScale = UiScaling.FitToWidth(UiScaling.GetScale(menu), 260, Math.Max(1, width - 12));
            menu.LayoutTransform = new ScaleTransform(effectiveScale, effectiveScale);
            // The scroll viewers keep every command reachable on short screens.
            // Resources also flow into submenu popup children; WPF handles their
            // edge flipping and placement in the monitor work area.
            menu.Resources["MenuMaxHeight"] = Math.Max(1, Math.Min(520, (height - 12) / effectiveScale));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        public static bool IsDark(string theme)
        {
            if (theme == "dark") return true;
            if (theme == "light") return false;
            try
            {
                object value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return value is int && (int)value == 0;
            }
            catch { return false; }
        }

        private static SolidColorBrush Brush(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        // Templates retain MenuItem/PART_Popup so WPF owns keyboard navigation,
        // submenu timing, mouse capture, outside-click and Escape dismissal.
        private const string Templates = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='{x:Type ContextMenu}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Background' Value='{DynamicResource MenuSurface}'/>
    <Setter Property='Foreground' Value='{DynamicResource MenuInk}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource MenuLine}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='FontFamily' Value='Segoe UI, Microsoft YaHei UI, Microsoft YaHei'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='MinWidth' Value='240'/>
    <Setter Property='MaxWidth' Value='260'/>
    <Setter Property='MaxHeight' Value='{DynamicResource MenuMaxHeight}'/>
    <Setter Property='Padding' Value='4'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='UseLayoutRounding' Value='True'/>
    <Setter Property='KeyboardNavigation.DirectionalNavigation' Value='Cycle'/>
    <Setter Property='KeyboardNavigation.TabNavigation' Value='Cycle'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ContextMenu}'>
          <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='10' Padding='{TemplateBinding Padding}'>
            <ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'
                          Focusable='False' Background='Transparent' BorderThickness='0'>
              <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle' SnapsToDevicePixels='True'/>
            </ScrollViewer>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='{x:Type MenuItem}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource MenuInk}'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='FontFamily' Value='Segoe UI, Microsoft YaHei UI, Microsoft YaHei'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Padding' Value='12,8,11,8'/>
    <Setter Property='HeaderTemplate'>
      <Setter.Value>
        <DataTemplate><TextBlock Text='{Binding}' TextTrimming='CharacterEllipsis'/></DataTemplate>
      </Setter.Value>
    </Setter>
    <Setter Property='MinHeight' Value='36'/>
    <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='ToolTip' Value='{Binding Header, RelativeSource={RelativeSource Self}}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type MenuItem}'>
          <Grid>
            <Border x:Name='ItemSurface' Background='{TemplateBinding Background}' CornerRadius='6' Padding='{TemplateBinding Padding}'>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='16'/>
                </Grid.ColumnDefinitions>
                <ContentPresenter x:Name='HeaderPresenter' ContentSource='Header' RecognizesAccessKey='True'
                                  VerticalAlignment='Center' Margin='0,0,4,0' ClipToBounds='True'/>
                <TextBlock Grid.Column='1' Text='{TemplateBinding InputGestureText}' Margin='8,0,5,0' FontSize='12'
                           Foreground='{DynamicResource MenuMuted}' VerticalAlignment='Center'/>
                <Path x:Name='CheckMark' Grid.Column='2' Data='M 1,5 L 4,8 L 10,1' Stroke='{TemplateBinding Foreground}'
                      StrokeThickness='1.5' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'
                      Width='11' Height='10' HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/>
                <Path x:Name='SubmenuArrow' Grid.Column='2' Data='M 2,1 L 6,5 L 2,9' Stroke='{TemplateBinding Foreground}'
                      StrokeThickness='1.2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'
                      Width='8' Height='10' HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/>
              </Grid>
            </Border>
            <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='2' VerticalOffset='-4' Focusable='False'
                   AllowsTransparency='True' PopupAnimation='None' IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>
              <Border Background='{DynamicResource MenuSurface}' BorderBrush='{DynamicResource MenuLine}' BorderThickness='1'
                      CornerRadius='10' Padding='4' MinWidth='240' MaxWidth='260' MaxHeight='{DynamicResource MenuMaxHeight}'>
                <ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'
                              Focusable='False' Background='Transparent' BorderThickness='0'>
                  <ItemsPresenter x:Name='ItemsPresenter' KeyboardNavigation.DirectionalNavigation='Cycle' SnapsToDevicePixels='True'/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='ItemSurface' Property='Background' Value='{DynamicResource MenuHover}'/>
            </Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='ItemSurface' Property='Background' Value='{DynamicResource MenuHover}'/>
            </Trigger>
            <Trigger Property='IsSubmenuOpen' Value='True'>
              <Setter TargetName='ItemSurface' Property='Background' Value='{DynamicResource MenuHover}'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='CheckMark' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='HasItems' Value='True'>
              <Setter TargetName='SubmenuArrow' Property='Visibility' Value='Visible'/>
              <Setter TargetName='CheckMark' Property='Visibility' Value='Collapsed'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Foreground' Value='{DynamicResource MenuDisabled}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='{x:Type Separator}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type Separator}'>
          <Border Height='1' Margin='9,4,9,4' Background='{DynamicResource MenuLine}'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";
    }

    // A tray menu needs an ordinary activatable HWND; the desktop widget keeps
    // its own Z order locked. This transparent anchor exists only while open.
    internal sealed class TrayMenuHost : Window
    {
        private readonly ContextMenu menu;
        private readonly Border anchor;
        private bool closing;

        public TrayMenuHost(ContextMenu menu)
        {
            this.menu = menu;
            Title = "桌面待办 · 菜单";
            Width = Height = 1;
            Left = Top = -32000;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Content = anchor = new Border { Background = Brushes.Transparent };
            menu.Placement = PlacementMode.Bottom;
            menu.PlacementTarget = anchor;
            menu.PlacementRectangle = new Rect(0, 0, 1, 1);
            menu.VerticalOffset = 2;
            menu.Closed += MenuClosed;
            Deactivated += delegate
            {
                // ContextMenu/submenu HWNDs belong to this process. Switching
                // to another app must dismiss even if mouse capture was lost.
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (closing || !menu.IsOpen) return;
                    uint process;
                    Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out process);
                    if (process != Native.GetCurrentProcessId()) menu.IsOpen = false;
                }), DispatcherPriority.Background);
            };
            Closed += delegate { menu.Closed -= MenuClosed; menu.PlacementTarget = null; };
        }

        public void OpenAtCursor()
        {
            NativePoint point;
            if (!Native.GetCursorPos(out point)) return;
            Show();
            IntPtr handle = new WindowInteropHelper(this).Handle;
            // SetWindowPos takes physical pixels; the live placement target then
            // lets WPF select the monitor DPI and flip/clamp at screen edges.
            Native.SetWindowPos(handle, new IntPtr(-1), point.X, point.Y, 0, 0, 0x0211);
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (closing) return;
                Activate();
                Native.SetForegroundWindow(handle);
                menu.IsOpen = true;
                menu.Focus();
            }), DispatcherPriority.Loaded);
        }

        private void MenuClosed(object sender, RoutedEventArgs args)
        {
            if (closing) return;
            closing = true;
            // Allow MenuBase to release capture and restore keyboard focus first.
            Dispatcher.BeginInvoke(new Action(delegate { Close(); }), DispatcherPriority.Normal);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }

        private static class Native
        {
            [DllImport("user32.dll")] public static extern bool GetCursorPos(out NativePoint point);
            [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
            [DllImport("kernel32.dll")] public static extern uint GetCurrentProcessId();
            [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        }
    }
}
