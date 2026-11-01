using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopTodo
{
    public sealed class TodoEditResult
    {
        public string Title { get; set; }
        public string DueAt { get; set; }
    }

    public sealed class SettingsEditResult
    {
        public double BackgroundOpacity { get; set; }
        public bool Animations { get; set; }
        public bool StartWithWindows { get; set; }
        public string Theme { get; set; }
    }

    public static class TodoDialogs
    {
        private sealed class Palette
        {
            public string Surface, Ink, Muted, Field, Line, Accent, AccentInk, Selected, Error;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        private static Brush B(string color) { return (SolidColorBrush)new BrushConverter().ConvertFromString(color); }

        private static bool IsDark(Window owner, string theme)
        {
            if (theme == "dark") return true;
            if (theme == "light") return false;
            if (theme == null && owner != null && owner.ReadLocalValue(Control.ForegroundProperty) != DependencyProperty.UnsetValue)
            {
                SolidColorBrush brush = owner.Foreground as SolidColorBrush;
                if (brush != null) return (brush.Color.R * 299 + brush.Color.G * 587 + brush.Color.B * 114) / 1000 > 155;
            }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value != null && Convert.ToInt32(value, CultureInfo.InvariantCulture) == 0;
                }
            }
            catch { return false; }
        }

        private static Palette Colors(Window owner, string theme)
        {
            bool dark = IsDark(owner, theme);
            return new Palette
            {
                Surface = dark ? "#D928302B" : "#D9F8FAF7",
                Ink = dark ? "#EDF2ED" : "#29382E",
                Muted = dark ? "#A8B9AD" : "#687D6E",
                Field = dark ? "#344139" : "#F0F4EF",
                Line = dark ? "#4F6255" : "#D9E3D8",
                Accent = dark ? "#B3CDB8" : "#466B50",
                AccentInk = dark ? "#263B2C" : "#FFFFFF",
                Selected = dark ? "#405C48" : "#E4EEE3",
                Error = dark ? "#F5B6AC" : "#A4473B"
            };
        }

        // All input surfaces share one small visual vocabulary. These templates keep the
        // native TextBox/DatePicker/Slider input and accessibility behavior intact.
        private static ResourceDictionary Styles(Palette p)
        {
            string xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
 xmlns:primitives='clr-namespace:System.Windows.Controls.Primitives;assembly=PresentationFramework'>
 <SolidColorBrush x:Key='Ink' Color='@INK@'/>
 <SolidColorBrush x:Key='Muted' Color='@MUTED@'/>
 <SolidColorBrush x:Key='Field' Color='@FIELD@'/>
 <SolidColorBrush x:Key='Line' Color='@LINE@'/>
 <SolidColorBrush x:Key='Accent' Color='@ACCENT@'/>
 <SolidColorBrush x:Key='AccentInk' Color='@ACCENTINK@'/>
 <SolidColorBrush x:Key='Selected' Color='@SELECTED@'/>
 <SolidColorBrush x:Key='Surface' Color='@SURFACE@'/>
 <Style x:Key='Secondary' TargetType='{x:Type Button}'>
  <Setter Property='Foreground' Value='{StaticResource Ink}'/>
  <Setter Property='Background' Value='{StaticResource Field}'/>
  <Setter Property='BorderBrush' Value='Transparent'/>
  <Setter Property='BorderThickness' Value='1'/>
  <Setter Property='Padding' Value='14,8'/>
  <Setter Property='Cursor' Value='Hand'/>
  <Setter Property='HorizontalContentAlignment' Value='Center'/>
  <Setter Property='VerticalContentAlignment' Value='Center'/>
  <Setter Property='Template'><Setter.Value>
   <ControlTemplate TargetType='{x:Type Button}'>
    <Border x:Name='Shell' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'>
     <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' RecognizesAccessKey='True'/>
    </Border>
    <ControlTemplate.Triggers>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Shell' Property='Opacity' Value='.82'/></Trigger>
     <Trigger Property='IsPressed' Value='True'><Setter TargetName='Shell' Property='Opacity' Value='.65'/></Trigger>
     <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Shell' Property='BorderBrush' Value='{StaticResource Accent}'/></Trigger>
     <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Shell' Property='Opacity' Value='.45'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate>
  </Setter.Value></Setter>
 </Style>
 <Style x:Key='Primary' TargetType='{x:Type Button}' BasedOn='{StaticResource Secondary}'>
  <Setter Property='Background' Value='{StaticResource Accent}'/>
  <Setter Property='Foreground' Value='{StaticResource AccentInk}'/>
 </Style>
 <Style x:Key='Quiet' TargetType='{x:Type Button}' BasedOn='{StaticResource Secondary}'>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Foreground' Value='{StaticResource Muted}'/>
  <Setter Property='Padding' Value='5'/>
 </Style>
 <ControlTemplate x:Key='InputTemplate' TargetType='{x:Type TextBox}'>
  <Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8'>
   <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' Focusable='False'/>
  </Border>
  <ControlTemplate.Triggers>
   <Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{StaticResource Accent}'/></Trigger>
   <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Frame' Property='Opacity' Value='.5'/></Trigger>
  </ControlTemplate.Triggers>
 </ControlTemplate>
 <Style x:Key='Input' TargetType='{x:Type TextBox}'>
  <Setter Property='Foreground' Value='{StaticResource Ink}'/>
  <Setter Property='CaretBrush' Value='{StaticResource Ink}'/>
  <Setter Property='Background' Value='{StaticResource Field}'/>
  <Setter Property='BorderBrush' Value='Transparent'/>
  <Setter Property='BorderThickness' Value='1'/>
  <Setter Property='Padding' Value='11,9'/>
  <Setter Property='VerticalContentAlignment' Value='Center'/>
  <Setter Property='SelectionBrush' Value='{StaticResource Selected}'/>
  <Setter Property='Template' Value='{StaticResource InputTemplate}'/>
 </Style>
 <Style x:Key='DateInput' TargetType='{x:Type primitives:DatePickerTextBox}' BasedOn='{StaticResource Input}'>
  <Setter Property='Padding' Value='0'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='BorderThickness' Value='0'/>
 </Style>
 <Style x:Key='Check' TargetType='{x:Type CheckBox}'>
  <Setter Property='Foreground' Value='{StaticResource Ink}'/>
  <Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value>
   <ControlTemplate TargetType='{x:Type CheckBox}'>
    <Grid Background='Transparent'>
     <Grid.ColumnDefinitions><ColumnDefinition Width='21'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
     <Border x:Name='Box' Width='19' Height='19' CornerRadius='5' BorderBrush='{StaticResource Line}' BorderThickness='1' Background='{StaticResource Field}' VerticalAlignment='Center'>
      <TextBlock x:Name='Tick' Text='✓' Foreground='{StaticResource AccentInk}' FontSize='13' HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/>
     </Border>
     <ContentPresenter Grid.Column='1' Margin='9,0,0,0' VerticalAlignment='Center' RecognizesAccessKey='True'/>
    </Grid>
    <ControlTemplate.Triggers>
     <Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='{StaticResource Accent}'/><Setter TargetName='Box' Property='BorderBrush' Value='{StaticResource Accent}'/><Setter TargetName='Tick' Property='Visibility' Value='Visible'/></Trigger>
     <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='{StaticResource Accent}'/></Trigger>
     <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.5'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate>
  </Setter.Value></Setter>
 </Style>
 <Style x:Key='ThemeChoice' TargetType='{x:Type RadioButton}'>
  <Setter Property='Foreground' Value='{StaticResource Muted}'/>
  <Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value>
   <ControlTemplate TargetType='{x:Type RadioButton}'>
    <Border x:Name='Choice' Padding='8,9' CornerRadius='7' BorderThickness='1' BorderBrush='Transparent' Background='Transparent'>
     <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
    </Border>
    <ControlTemplate.Triggers>
     <Trigger Property='IsChecked' Value='True'><Setter TargetName='Choice' Property='Background' Value='{StaticResource Selected}'/><Setter Property='Foreground' Value='{StaticResource Ink}'/></Trigger>
     <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Choice' Property='BorderBrush' Value='{StaticResource Accent}'/></Trigger>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Choice' Property='BorderBrush' Value='{StaticResource Line}'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate>
  </Setter.Value></Setter>
 </Style>
 <ControlTemplate x:Key='RailButton' TargetType='{x:Type RepeatButton}'>
  <Grid Background='Transparent'><Border Height='4' CornerRadius='2' Background='{TemplateBinding Background}' VerticalAlignment='Center'/></Grid>
 </ControlTemplate>
 <Style x:Key='OpacitySlider' TargetType='{x:Type Slider}'>
  <Setter Property='Height' Value='28'/>
  <Setter Property='Template'><Setter.Value>
   <ControlTemplate TargetType='{x:Type Slider}'>
    <Grid Margin='0,0,0,0' Background='Transparent'>
     <Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' IsDirectionReversed='{TemplateBinding IsDirectionReversed}'>
      <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' Background='{StaticResource Accent}' Template='{StaticResource RailButton}' Focusable='False'/></Track.DecreaseRepeatButton>
      <Track.Thumb><Thumb Width='16' Height='16' Cursor='Hand'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border CornerRadius='8' Background='{StaticResource Accent}' BorderBrush='{StaticResource Surface}' BorderThickness='2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
      <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}' Background='{StaticResource Line}' Template='{StaticResource RailButton}' Focusable='False'/></Track.IncreaseRepeatButton>
     </Track>
    </Grid>
   </ControlTemplate>
  </Setter.Value></Setter>
 </Style>
 <Style x:Key='CalendarDay' TargetType='{x:Type CalendarDayButton}'>
  <Setter Property='Foreground' Value='{StaticResource Ink}'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='MinWidth' Value='34'/>
  <Setter Property='MinHeight' Value='32'/>
  <Setter Property='Template'><Setter.Value>
   <ControlTemplate TargetType='{x:Type CalendarDayButton}'>
    <Border x:Name='Day' CornerRadius='6' Background='{TemplateBinding Background}' Margin='1' BorderThickness='1' BorderBrush='Transparent'>
     <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
    </Border>
    <ControlTemplate.Triggers>
     <Trigger Property='IsInactive' Value='True'><Setter Property='Foreground' Value='{StaticResource Muted}'/><Setter Property='Opacity' Value='.55'/></Trigger>
     <Trigger Property='IsToday' Value='True'><Setter TargetName='Day' Property='BorderBrush' Value='{StaticResource Line}'/></Trigger>
     <Trigger Property='IsSelected' Value='True'><Setter TargetName='Day' Property='Background' Value='{StaticResource Accent}'/><Setter Property='Foreground' Value='{StaticResource AccentInk}'/></Trigger>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Day' Property='BorderBrush' Value='{StaticResource Accent}'/></Trigger>
     <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Day' Property='BorderBrush' Value='{StaticResource Accent}'/></Trigger>
     <Trigger Property='IsBlackedOut' Value='True'><Setter Property='Opacity' Value='.3'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate>
  </Setter.Value></Setter>
 </Style>
 <Style x:Key='CalendarMonth' TargetType='{x:Type CalendarButton}'>
  <Setter Property='Foreground' Value='{StaticResource Ink}'/>
  <Setter Property='Template'><Setter.Value>
   <ControlTemplate TargetType='{x:Type CalendarButton}'>
    <Border x:Name='Month' CornerRadius='6' Background='Transparent' Padding='6,9' Margin='2'><ContentPresenter HorizontalAlignment='Center'/></Border>
    <ControlTemplate.Triggers>
     <Trigger Property='HasSelectedDays' Value='True'><Setter TargetName='Month' Property='Background' Value='{StaticResource Selected}'/></Trigger>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Month' Property='Background' Value='{StaticResource Field}'/></Trigger>
     <Trigger Property='IsInactive' Value='True'><Setter Property='Opacity' Value='.5'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate>
  </Setter.Value></Setter>
 </Style>
 <Style x:Key='CalendarBody' TargetType='{x:Type CalendarItem}'>
  <Setter Property='Template'><Setter.Value>
   <ControlTemplate TargetType='{x:Type CalendarItem}'>
    <Border Background='{StaticResource Surface}' BorderBrush='{StaticResource Line}' BorderThickness='1' CornerRadius='10' Padding='10'>
     <Grid x:Name='PART_Root'>
      <Grid.Resources><DataTemplate x:Key='{x:Static CalendarItem.DayTitleTemplateResourceKey}'><TextBlock Text='{Binding}' HorizontalAlignment='Center' Foreground='{StaticResource Muted}' FontSize='12' Margin='0,5'/></DataTemplate></Grid.Resources>
      <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
      <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
      <Button x:Name='PART_PreviousButton' Content='‹' Style='{StaticResource Quiet}' Width='30' Height='30' FontSize='20'/>
      <Button x:Name='PART_HeaderButton' Grid.Column='1' Style='{StaticResource Quiet}' Margin='0,0,0,5'/>
      <Button x:Name='PART_NextButton' Grid.Column='2' Content='›' Style='{StaticResource Quiet}' Width='30' Height='30' FontSize='20'/>
      <Grid x:Name='PART_MonthView' Grid.Row='1' Grid.ColumnSpan='3'>
       <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/></Grid.RowDefinitions>
       <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
      </Grid>
      <Grid x:Name='PART_YearView' Grid.Row='1' Grid.ColumnSpan='3' Visibility='Hidden'>
       <Grid.RowDefinitions><RowDefinition/><RowDefinition/><RowDefinition/></Grid.RowDefinitions>
       <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
      </Grid>
     </Grid>
    </Border>
   </ControlTemplate>
  </Setter.Value></Setter>
 </Style>
 <Style x:Key='PickerCalendar' TargetType='{x:Type Calendar}'>
  <Setter Property='FontSize' Value='14'/>
  <Setter Property='Foreground' Value='{StaticResource Ink}'/>
  <Setter Property='Background' Value='{StaticResource Surface}'/>
  <Setter Property='BorderThickness' Value='0'/>
  <Setter Property='CalendarItemStyle' Value='{StaticResource CalendarBody}'/>
  <Setter Property='CalendarDayButtonStyle' Value='{StaticResource CalendarDay}'/>
  <Setter Property='CalendarButtonStyle' Value='{StaticResource CalendarMonth}'/>
 </Style>
 <Style x:Key='DatePicker' TargetType='{x:Type DatePicker}'>
  <Setter Property='Foreground' Value='{StaticResource Ink}'/>
  <Setter Property='CalendarStyle' Value='{StaticResource PickerCalendar}'/>
  <Setter Property='Template'><Setter.Value>
   <ControlTemplate TargetType='{x:Type DatePicker}'>
    <Grid x:Name='PART_Root'>
     <Border x:Name='DateFrame' Background='{StaticResource Field}' CornerRadius='8' BorderBrush='Transparent' BorderThickness='1'>
      <Grid Margin='11,7,5,7'>
       <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='30'/></Grid.ColumnDefinitions>
       <primitives:DatePickerTextBox x:Name='PART_TextBox' Style='{StaticResource DateInput}' VerticalContentAlignment='Center'/>
       <Button x:Name='PART_Button' Grid.Column='1' Content='⌄' FontSize='16' Style='{StaticResource Quiet}' AutomationProperties.Name='打开日历'/>
      </Grid>
     </Border>
     <Popup x:Name='PART_Popup' Placement='Bottom' StaysOpen='False' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade' VerticalOffset='6'/>
    </Grid>
    <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='DateFrame' Property='BorderBrush' Value='{StaticResource Accent}'/></Trigger></ControlTemplate.Triggers>
   </ControlTemplate>
  </Setter.Value></Setter>
 </Style>
</ResourceDictionary>";
            xaml = xaml.Replace("@INK@", p.Ink).Replace("@MUTED@", p.Muted).Replace("@FIELD@", p.Field)
                .Replace("@LINE@", p.Line).Replace("@ACCENT@", p.Accent).Replace("@ACCENTINK@", p.AccentInk)
                .Replace("@SELECTED@", p.Selected).Replace("@SURFACE@", p.Surface);
            return (ResourceDictionary)XamlReader.Parse(xaml);
        }

        private static Window CreateWindow(Window owner, string title, Palette p)
        {
            Window window = new Window
            {
                Title = title, Width = 400, SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true, Background = Brushes.Transparent,
                Foreground = B(p.Ink), ShowInTaskbar = false, Topmost = true,
                FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 15,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            if (owner != null && owner.IsVisible)
            {
                IntPtr hwnd = new WindowInteropHelper(owner).Handle;
                // A WorkerW/desktop child cannot safely be a modal WPF owner.
                if (hwnd != IntPtr.Zero && GetParent(hwnd) == IntPtr.Zero && (GetWindowLong(hwnd, -16) & 0x40000000) == 0)
                    window.Owner = owner;
            }
            // An editor opened by a desktop child is an independent top-level window.
            // Give it a temporary taskbar entry so Alt+Tab can always bring it back.
            window.ShowInTaskbar = window.Owner == null;
            window.Resources = Styles(p);
            window.Loaded += delegate { Position(window, owner); window.Activate(); };
            window.SizeChanged += delegate { if (window.IsLoaded) Position(window, owner); };
            return window;
        }

        private static void Position(Window window, Window anchor)
        {
            Rect work = SystemParameters.WorkArea;
            Rect near = Rect.Empty;
            PresentationSource source = PresentationSource.FromVisual(window);
            Matrix fromDevice = source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice : Matrix.Identity;
            IntPtr anchorHwnd = anchor == null ? IntPtr.Zero : new WindowInteropHelper(anchor).Handle;
            NativeRect native;
            if (anchorHwnd != IntPtr.Zero && GetWindowRect(anchorHwnd, out native))
            {
                Point topLeft = fromDevice.Transform(new Point(native.Left, native.Top));
                Point bottomRight = fromDevice.Transform(new Point(native.Right, native.Bottom));
                near = new Rect(topLeft, bottomRight);
                MonitorInfo info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (GetMonitorInfo(MonitorFromWindow(anchorHwnd, 2), ref info))
                    work = new Rect(fromDevice.Transform(new Point(info.Work.Left, info.Work.Top)), fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom)));
            }
            double width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            double height = window.ActualHeight > 0 ? window.ActualHeight : 360;
            double left = near.IsEmpty ? work.Left + (work.Width - width) / 2 : near.Left - width - 12;
            double top = near.IsEmpty ? work.Top + (work.Height - height) / 2 : near.Top;
            if (!near.IsEmpty && left < work.Left + 12) { left = near.Right - width; top = near.Bottom + 12; }
            window.Left = Math.Max(work.Left + 12, Math.Min(left, work.Right - width - 12));
            window.Top = Math.Max(work.Top + 12, Math.Min(top, work.Bottom - height - 12));
        }

        private static StackPanel Body(Window window, string title, string subtitle, Palette p)
        {
            Border shell = new Border { CornerRadius = new CornerRadius(10), Background = B(p.Surface), BorderBrush = B(p.Line), BorderThickness = new Thickness(1) };
            StackPanel body = new StackPanel { Margin = new Thickness(18, 15, 18, 18) };
            shell.Child = body;
            window.Content = shell;
            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel titles = new StackPanel();
            titles.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Medium, Foreground = B(p.Ink) });
            if (!string.IsNullOrEmpty(subtitle)) titles.Children.Add(new TextBlock { Text = subtitle, Foreground = B(p.Muted), FontSize = 13, Margin = new Thickness(0, 5, 10, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
            header.Children.Add(titles);
            Button close = Button(window, "×", false);
            close.Style = (Style)window.Resources["Quiet"];
            close.FontSize = 20;
            close.Width = 28;
            close.Height = 28;
            close.MinWidth = 0;
            close.Margin = new Thickness(6, -3, -3, 0);
            close.VerticalAlignment = VerticalAlignment.Top;
            close.IsCancel = true;
            close.Click += delegate { window.Close(); };
            AutomationProperties.SetName(close, "关闭");
            Grid.SetColumn(close, 1);
            header.Children.Add(close);
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
            {
                if (args.OriginalSource == header || args.OriginalSource is TextBlock)
                {
                    try { window.DragMove(); } catch (InvalidOperationException) { }
                }
            };
            body.Children.Add(header);
            return body;
        }

        private static TextBlock Label(string text, Palette p)
        {
            return new TextBlock { Text = text, Foreground = B(p.Muted), FontSize = 13, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        }
        private static TextBox Input(Window window, string name)
        {
            TextBox input = new TextBox { Style = (Style)window.Resources["Input"] };
            AutomationProperties.SetName(input, name);
            return input;
        }
        private static Button Button(Window window, string label, bool primary)
        {
            Button button = new Button { Content = label, Style = (Style)window.Resources[primary ? "Primary" : "Secondary"], MinWidth = 68 };
            AutomationProperties.SetName(button, label);
            return button;
        }
        private static TextBlock ErrorText(StackPanel body, Palette p)
        {
            TextBlock error = new TextBlock { Foreground = B(p.Error), TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
            AutomationProperties.SetName(error, "输入提示");
            body.Children.Add(error);
            return error;
        }
        private static void Error(TextBlock text, string message) { text.Text = message; text.Visibility = Visibility.Visible; }
        private static Button Actions(Window window, StackPanel body, string saveLabel)
        {
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            Button cancel = Button(window, "取消", false);
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(0, 0, 8, 0);
            Button save = Button(window, saveLabel, true);
            actions.Children.Add(cancel);
            actions.Children.Add(save);
            body.Children.Add(actions);
            return save;
        }

        public static string PromptName(Window owner, string title, string initial)
        {
            Palette p = Colors(owner, null);
            Window window = CreateWindow(owner, title, p);
            window.Width = 380;
            StackPanel body = Body(window, title, null, p);
            TextBox name = Input(window, "分类名称");
            name.Text = initial ?? "";
            name.MaxLength = 32;
            body.Children.Add(name);
            TextBlock error = ErrorText(body, p);
            Button save = Actions(window, body, "保存");
            save.IsDefault = true;
            string result = null;
            save.Click += delegate
            {
                string value = name.Text.Trim();
                if (value.Length == 0) { Error(error, "给这个分类起一个名字吧。"); name.Focus(); return; }
                result = value;
                window.DialogResult = true;
            };
            window.Loaded += delegate { name.Focus(); name.SelectAll(); };
            window.ShowDialog();
            return result;
        }

        public static TodoEditResult EditTodo(Window owner, TodoItem item, string categoryName)
        {
            Palette p = Colors(owner, null);
            string heading = item == null ? "添加事项" : "编辑事项";
            Window window = CreateWindow(owner, heading, p);
            StackPanel body = Body(window, heading, null, p);
            TextBox title = Input(window, "事项内容");
            title.Text = item == null ? "" : item.Title ?? "";
            title.AcceptsReturn = true;
            title.TextWrapping = TextWrapping.Wrap;
            title.MinHeight = 74;
            title.MaxHeight = 132;
            title.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            title.MaxLength = 500;
            title.VerticalContentAlignment = VerticalAlignment.Top;
            body.Children.Add(title);

            DateTimeOffset existing = DateTimeOffset.Now;
            bool hasDue = item != null && !string.IsNullOrWhiteSpace(item.DueAt) && DateTimeOffset.TryParse(item.DueAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out existing);
            DateTime localDue = hasDue ? existing.LocalDateTime : DateTime.Today.AddHours(23).AddMinutes(59);
            CheckBox enableDue = new CheckBox { Content = "截止时间", Style = (Style)window.Resources["Check"], IsChecked = hasDue, Margin = new Thickness(0, 12, 0, 0) };
            AutomationProperties.SetName(enableDue, "设置截止时间");
            body.Children.Add(enableDue);

            StackPanel dueArea = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Visibility = hasDue ? Visibility.Visible : Visibility.Collapsed };
            Grid dateRow = new Grid();
            dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            DatePicker date = new DatePicker
            {
                SelectedDate = localDue.Date, SelectedDateFormat = DatePickerFormat.Short,
                Language = XmlLanguage.GetLanguage("zh-CN"), Style = (Style)window.Resources["DatePicker"]
            };
            AutomationProperties.SetName(date, "截止日期");
            date.DateValidationError += delegate(object sender, DatePickerDateValidationErrorEventArgs e) { e.ThrowException = false; };
            dateRow.Children.Add(date);
            TextBox time = Input(window, "截止时间，时分");
            time.Text = localDue.ToString("HH:mm", CultureInfo.InvariantCulture);
            time.MaxLength = 5;
            time.HorizontalContentAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(time, 2);
            dateRow.Children.Add(time);
            dueArea.Children.Add(dateRow);
            body.Children.Add(dueArea);
            enableDue.Checked += delegate
            {
                dueArea.Visibility = Visibility.Visible;
                if (SystemParameters.ClientAreaAnimation)
                    dueArea.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            };
            enableDue.Unchecked += delegate { dueArea.BeginAnimation(UIElement.OpacityProperty, null); dueArea.Opacity = 1; dueArea.Visibility = Visibility.Collapsed; };
            TextBlock error = ErrorText(body, p);
            Button save = Actions(window, body, item == null ? "添加事项" : "保存修改");
            // Do not make Save the default: Enter remains available to multiline input and IME.
            TodoEditResult result = null;
            save.Click += delegate
            {
                string text = title.Text.Trim();
                if (text.Length == 0) { Error(error, "先写下一件要做的事。"); title.Focus(); return; }
                string dueAt = null;
                if (enableDue.IsChecked == true)
                {
                    DateTime selected;
                    if (string.IsNullOrWhiteSpace(date.Text) || !DateTime.TryParse(date.Text, CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.AllowWhiteSpaces, out selected))
                    { Error(error, "请选择一个有效的截止日期。"); date.Focus(); return; }
                    DateTime parsedTime;
                    string timeText = string.IsNullOrWhiteSpace(time.Text) ? "23:59" : time.Text.Trim();
                    if (!DateTime.TryParseExact(timeText, new string[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedTime))
                    { Error(error, "时间可以写成 18:30，范围为 00:00—23:59。"); time.Focus(); return; }
                    DateTime value = DateTime.SpecifyKind(selected.Date.AddHours(parsedTime.Hour).AddMinutes(parsedTime.Minute), DateTimeKind.Unspecified);
                    if (TimeZoneInfo.Local.IsInvalidTime(value)) { Error(error, "这个时间暂不可用，请换一个时间。"); return; }
                    dueAt = new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value)).ToString("o", CultureInfo.InvariantCulture);
                }
                result = new TodoEditResult { Title = text, DueAt = dueAt };
                window.DialogResult = true;
            };
            window.Loaded += delegate { title.Focus(); title.CaretIndex = title.Text.Length; };
            window.ShowDialog();
            return result;
        }

        public static SettingsEditResult EditSettings(Window owner, AppSettings settings)
        {
            Palette p = Colors(owner, settings.Theme);
            Window window = CreateWindow(owner, "外观与设置", p);
            StackPanel body = Body(window, "外观与设置", null, p);
            Grid opacityHeader = new Grid();
            opacityHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            opacityHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            opacityHeader.Children.Add(Label("底板不透明度", p));
            TextBlock valueLabel = new TextBlock { Foreground = B(p.Ink), FontSize = 13, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(valueLabel, 1);
            opacityHeader.Children.Add(valueLabel);
            body.Children.Add(opacityHeader);
            Slider opacity = new Slider
            {
                Minimum = .10, Maximum = .95, TickFrequency = .05, IsSnapToTickEnabled = true,
                SmallChange = .05, LargeChange = .10, Style = (Style)window.Resources["OpacitySlider"],
                Value = Math.Max(.10, Math.Min(.95, settings.BackgroundOpacity)), Margin = new Thickness(0, 0, 0, 4)
            };
            AutomationProperties.SetName(opacity, "底板不透明度");
            valueLabel.Text = Math.Round(opacity.Value * 100).ToString(CultureInfo.InvariantCulture) + "%";
            opacity.ValueChanged += delegate { valueLabel.Text = Math.Round(opacity.Value * 100).ToString(CultureInfo.InvariantCulture) + "%"; };
            body.Children.Add(opacity);
            TextBlock themeLabel = Label("外观主题", p);
            themeLabel.Margin = new Thickness(0, 10, 0, 8);
            body.Children.Add(themeLabel);
            UniformGrid themeChoices = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 0) };
            RadioButton auto = ThemeChoice(window, "自动", "auto", settings.Theme != "light" && settings.Theme != "dark");
            RadioButton light = ThemeChoice(window, "浅色", "light", settings.Theme == "light");
            RadioButton dark = ThemeChoice(window, "深色", "dark", settings.Theme == "dark");
            themeChoices.Children.Add(auto); themeChoices.Children.Add(light); themeChoices.Children.Add(dark);
            body.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = B(p.Field), Padding = new Thickness(3), Child = themeChoices, Margin = new Thickness(0, 0, 0, 15) });
            CheckBox animation = new CheckBox { Content = "提醒动效", IsChecked = settings.Animations, Style = (Style)window.Resources["Check"] };
            AutomationProperties.SetName(animation, "紧急提醒动效");
            body.Children.Add(animation);
            animation.ToolTip = "进入紧急阶段时，短暂动一下。";
            CheckBox startup = new CheckBox { Content = "开机启动", IsChecked = settings.StartWithWindows, Style = (Style)window.Resources["Check"], Margin = new Thickness(0, 12, 0, 0) };
            AutomationProperties.SetName(startup, "开机启动");
            body.Children.Add(startup);
            startup.ToolTip = "登录 Windows 后自动显示待办。";
            Button save = Actions(window, body, "保存设置");
            save.IsDefault = true;
            SettingsEditResult result = null;
            save.Click += delegate
            {
                result = new SettingsEditResult
                {
                    BackgroundOpacity = Math.Round(opacity.Value, 2),
                    Animations = animation.IsChecked == true,
                    StartWithWindows = startup.IsChecked == true,
                    Theme = light.IsChecked == true ? "light" : dark.IsChecked == true ? "dark" : "auto"
                };
                window.DialogResult = true;
            };
            window.ShowDialog();
            return result;
        }
        private static RadioButton ThemeChoice(Window window, string title, string value, bool selected)
        {
            RadioButton button = new RadioButton { Content = title, Tag = value, IsChecked = selected, GroupName = "Theme", Style = (Style)window.Resources["ThemeChoice"] };
            AutomationProperties.SetName(button, title + "主题");
            return button;
        }

        public static bool Confirm(Window owner, string title, string message)
        {
            Palette p = Colors(owner, null);
            Window window = CreateWindow(owner, title, p);
            window.Width = 380;
            StackPanel body = Body(window, title, null, p);
            body.Children.Add(MessageContent(message, p));
            Button confirm = Actions(window, body, "确认");
            bool result = false;
            confirm.Click += delegate { result = true; window.DialogResult = true; };
            window.ShowDialog();
            return result;
        }

        public static void Notice(Window owner, string title, string message)
        {
            Palette p = Colors(owner, null);
            Window window = CreateWindow(owner, title, p);
            window.Width = 380;
            StackPanel body = Body(window, title, null, p);
            body.Children.Add(MessageContent(message, p));
            Button dismiss = Button(window, "知道了", true);
            dismiss.IsCancel = true;
            dismiss.IsDefault = true;
            dismiss.HorizontalAlignment = HorizontalAlignment.Right;
            dismiss.Margin = new Thickness(0, 16, 0, 0);
            body.Children.Add(dismiss);
            window.ShowDialog();
        }

        private static FrameworkElement MessageContent(string message, Palette p)
        {
            TextBlock text = new TextBlock
            {
                Text = message ?? "", Foreground = B(p.Muted), TextWrapping = TextWrapping.Wrap,
                FontSize = 15, LineHeight = 25
            };
            return new ScrollViewer
            {
                Content = text, MaxHeight = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }
    }
}
