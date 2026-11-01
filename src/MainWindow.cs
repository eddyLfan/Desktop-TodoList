using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DesktopTodo
{
    public sealed class MainWindow : Window
    {
        private readonly TodoState state;
        private readonly Action persist;
        private readonly Border shell;
        private readonly Grid layout;
        private readonly WrapPanel categoryPanel;
        private readonly ScrollViewer listScroll;
        private readonly StackPanel taskPanel;
        private readonly Button pinButton;
        private readonly Button menuButton;
        private readonly Button addButton;
        private readonly Button reminderButton;
        private readonly TextBlock reminderFace;
        private readonly TextBlock reminderText;
        private readonly StackPanel feedbackPanel;
        private readonly TextBlock feedbackText;
        private readonly Button undoButton;
        private readonly DispatcherTimer timer;
        private readonly DesktopLayer desktopLayer;
        private readonly Dictionary<string, TextBlock> countdowns = new Dictionary<string, TextBlock>();
        private readonly Dictionary<string, TextBlock> categoryUrgency = new Dictionary<string, TextBlock>();
        private readonly Dictionary<string, FrameworkElement> taskRows = new Dictionary<string, FrameworkElement>();
        private readonly List<PendingRow> pendingRows = new List<PendingRow>();
        private const string TodoDragFormat = "DesktopTodo.PendingOrder.v1";
        private Button pressedDragHandle;
        private Point dragStart;
        private bool dragging;
        private bool refreshAfterDrag;
        private string dragToken;
        private string dragItemId;
        private string dragCategoryId;
        private string dropTargetId;
        private bool dropAfter;
        private Border dropMarker;
        private DateTime lastDragScroll;
        private SolidColorBrush ink;
        private SolidColorBrush muted;
        private SolidColorBrush accent;
        private SolidColorBrush urgent;
        private SolidColorBrush overdue;
        private SolidColorBrush field;
        private bool dark;
        private bool locating;
        private DateTimeOffset lastTick;
        private int lastReminderStage = -1;
        private string reminderItemId;
        private string undoItemId;
        private string undoOldDeletedAt;
        private DateTimeOffset feedbackUntil;

        public event EventHandler ExitRequested;
        public event EventHandler OpenDataRequested;
        public event EventHandler SettingsChanged;
        public event EventHandler WindowHidden;

        public MainWindow(TodoState state, Action persist)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (persist == null) throw new ArgumentNullException("persist");
            this.state = state;
            this.persist = persist;
            Title = "桌面待办";
            Width = 400;
            MaxHeight = 600;
            MinHeight = 150;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, Microsoft YaHei");
            FontSize = 16;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            shell = new Border { Padding = new Thickness(20, 16, 20, 16), CornerRadius = new CornerRadius(12) };
            layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.Child = layout;
            Content = shell;

            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            categoryPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            ScrollViewer categoryScroll = new ScrollViewer
            {
                Content = categoryPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 96
            };
            header.Children.Add(categoryScroll);
            pinButton = TextButton("", "始终置顶", delegate { ToggleTopmost(); });
            pinButton.Width = 30;
            pinButton.Height = 30;
            pinButton.Padding = new Thickness(5, 4, 5, 4);
            pinButton.HorizontalContentAlignment = HorizontalAlignment.Center;
            pinButton.VerticalAlignment = VerticalAlignment.Top;
            pinButton.Margin = new Thickness(8, 0, 6, 0);
            Grid.SetColumn(pinButton, 1);
            header.Children.Add(pinButton);
            menuButton = TextButton("⋯", "待办菜单", delegate { OpenMainMenu(); });
            menuButton.FontSize = 22;
            menuButton.Width = 30;
            menuButton.Height = 30;
            menuButton.Padding = new Thickness(3, 0, 3, 2);
            menuButton.HorizontalContentAlignment = HorizontalAlignment.Center;
            menuButton.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(menuButton, 2);
            header.Children.Add(menuButton);
            Grid.SetRow(header, 0);
            layout.Children.Add(header);

            taskPanel = new StackPanel();
            listScroll = new ScrollViewer
            {
                Content = taskPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                CanContentScroll = false,
                MaxHeight = 410,
                Padding = new Thickness(0, 0, 3, 0)
            };
            listScroll.AllowDrop = true;
            listScroll.DragOver += DragOverList;
            listScroll.Drop += DropOnList;
            listScroll.DragLeave += delegate { ClearDropMarker(); };
            Grid.SetRow(listScroll, 1);
            layout.Children.Add(listScroll);

            reminderFace = new TextBlock { FontSize = 14, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            reminderFace.RenderTransform = new TranslateTransform();
            reminderText = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 306, VerticalAlignment = VerticalAlignment.Center };
            Grid reminderContent = new Grid();
            reminderContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            reminderContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            reminderContent.Children.Add(reminderFace);
            Grid.SetColumn(reminderText, 1);
            reminderContent.Children.Add(reminderText);
            reminderButton = TextButton(null, "查看最紧急事项", delegate { GoToReminder(); });
            reminderButton.Content = reminderContent;
            reminderButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            reminderButton.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(reminderButton, 2);
            layout.Children.Add(reminderButton);

            addButton = TextButton("＋ 添加事项", "添加事项", delegate { AddTodo(); });
            addButton.FontSize = 14;
            addButton.Margin = new Thickness(0, 14, 0, 0);
            addButton.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(addButton, 3);
            layout.Children.Add(addButton);

            feedbackPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0), Visibility = Visibility.Collapsed };
            feedbackText = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 292 };
            undoButton = TextButton("撤销", "撤销删除", delegate { UndoDelete(); });
            undoButton.FontSize = 13;
            undoButton.Margin = new Thickness(12, 0, 0, 0);
            feedbackPanel.Children.Add(feedbackText);
            feedbackPanel.Children.Add(undoButton);
            Grid.SetRow(feedbackPanel, 4);
            layout.Children.Add(feedbackPanel);

            CommandBindings.Add(new CommandBinding(ApplicationCommands.New, delegate { AddTodo(); }));
            InputBindings.Add(new KeyBinding(ApplicationCommands.New, new KeyGesture(Key.N, ModifierKeys.Control)));
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    ShowSettings();
                    e.Handled = true;
                }
            };
            Loaded += delegate { PositionAtTopRight(); Dispatcher.BeginInvoke(new Action(PositionAtTopRight), DispatcherPriority.ContextIdle); };
            SizeChanged += delegate { if (IsLoaded) PositionAtTopRight(); };
            SourceInitialized += delegate
            {
                HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                if (source != null) source.AddHook(WindowMessage);
            };
            IsVisibleChanged += delegate
            {
                if (!IsVisible && IsLoaded && WindowHidden != null) WindowHidden(this, EventArgs.Empty);
                if (IsVisible && IsLoaded) { UpdateClock(false); PositionAtTopRight(); }
            };
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += Tick;
            Closed += delegate { timer.Stop(); };
            desktopLayer = new DesktopLayer(this);
            ApplySettings();
            RefreshView();
            timer.Start();
        }

        private Button TextButton(string text, string accessibleName, Action action)
        {
            Button result = new Button
            {
                Content = text,
                Padding = new Thickness(2, 4, 2, 4),
                Margin = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand,
                MinHeight = 28,
                Foreground = ink ?? Brushes.Black,
                FontWeight = FontWeights.Normal
            };
            AutomationProperties.SetName(result, accessibleName);
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            content.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new System.Windows.Data.Binding("HorizontalContentAlignment") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromArgb(25, 114, 145, 121))));
            template.Triggers.Add(hover);
            result.Template = template;
            if (action != null) result.Click += delegate { action(); };
            return result;
        }

        private SolidColorBrush Brush(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        internal static ControlTemplate CreateTodoCheckTemplate(bool dark)
        {
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(FrameworkElement.WidthProperty, 26.0);
            root.SetValue(FrameworkElement.HeightProperty, 26.0);
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            var focus = new FrameworkElementFactory(typeof(Border));
            focus.Name = "FocusRing";
            focus.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            focus.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            focus.SetValue(Border.BorderBrushProperty, new SolidColorBrush(dark ? Color.FromRgb(172, 205, 181) : Color.FromRgb(68, 107, 80)));
            focus.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            root.AppendChild(focus);
            var box = new FrameworkElementFactory(typeof(Border));
            box.Name = "CheckSurface";
            box.SetValue(FrameworkElement.WidthProperty, 20.0);
            box.SetValue(FrameworkElement.HeightProperty, 20.0);
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            box.SetValue(Border.BorderBrushProperty, new SolidColorBrush(dark ? Color.FromRgb(152, 175, 159) : Color.FromRgb(111, 139, 120)));
            box.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            root.AppendChild(box);
            var mark = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            mark.Name = "CheckMark";
            mark.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M4.5,9.5 L8,13 L15.5,5.5"));
            mark.SetValue(System.Windows.Shapes.Path.StrokeProperty, Brushes.White);
            mark.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.8);
            mark.SetValue(System.Windows.Shapes.Path.StrokeStartLineCapProperty, PenLineCap.Round);
            mark.SetValue(System.Windows.Shapes.Path.StrokeEndLineCapProperty, PenLineCap.Round);
            mark.SetValue(System.Windows.Shapes.Path.StrokeLineJoinProperty, PenLineJoin.Round);
            mark.SetValue(FrameworkElement.WidthProperty, 20.0);
            mark.SetValue(FrameworkElement.HeightProperty, 20.0);
            mark.SetValue(UIElement.IsHitTestVisibleProperty, false);
            mark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            root.AppendChild(mark);
            var template = new ControlTemplate(typeof(CheckBox)) { VisualTree = root };
            var selected = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
            Brush selectedBrush = new SolidColorBrush(dark ? Color.FromRgb(78, 119, 91) : Color.FromRgb(68, 107, 80));
            selected.Setters.Add(new Setter(Border.BackgroundProperty, selectedBrush, "CheckSurface"));
            selected.Setters.Add(new Setter(Border.BorderBrushProperty, selectedBrush, "CheckSurface"));
            selected.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));
            template.Triggers.Add(selected);
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty,
                new SolidColorBrush(dark ? Color.FromRgb(196, 222, 202) : Color.FromRgb(49, 86, 61)), "CheckSurface"));
            template.Triggers.Add(hover);
            var hoverEmpty = new MultiTrigger();
            hoverEmpty.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            hoverEmpty.Conditions.Add(new Condition(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, false));
            hoverEmpty.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(dark ? Color.FromArgb(38, 122, 161, 134) : Color.FromArgb(22, 73, 115, 86)), "CheckSurface"));
            template.Triggers.Add(hoverEmpty);
            var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "FocusRing"));
            template.Triggers.Add(focused);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .45));
            template.Triggers.Add(disabled);
            return template;
        }

        private UIElement PinIcon(bool selected)
        {
            var icon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M6,3 H14 V5 H13 V9 L16,12 V13 H4 V12 L7,9 V5 H6 Z M10,13 V18"),
                Stroke = selected ? accent : muted,
                Fill = selected ? accent : Brushes.Transparent,
                StrokeThickness = 1.2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 20,
                Height = 20,
                RenderTransform = new RotateTransform(selected ? 0 : -25, 10, 10),
                IsHitTestVisible = false
            };
            return icon;
        }

        public void ApplySettings()
        {
            dark = state.Settings.Theme == "dark" || (state.Settings.Theme != "light" && SystemIsDark());
            ink = dark ? Brush(238, 243, 239) : Brush(36, 47, 40);
            muted = dark ? Brush(193, 206, 196) : Brush(77, 94, 83);
            accent = dark ? Brush(177, 211, 185) : Brush(55, 89, 64);
            urgent = dark ? Brush(248, 190, 132) : Brush(147, 69, 22);
            overdue = dark ? Brush(255, 171, 175) : Brush(158, 45, 52);
            field = dark ? Brush(37, 47, 40) : Brush(250, 252, 249);
            Foreground = ink;
            byte alpha = (byte)Math.Round(Math.Max(0.10, Math.Min(0.95, state.Settings.BackgroundOpacity)) * 255);
            shell.Background = new SolidColorBrush(dark ? Color.FromArgb(alpha, 25, 33, 28) : Color.FromArgb(alpha, 249, 252, 248));
            if (desktopLayer != null) desktopLayer.SetPinned(state.Settings.Topmost);
            pinButton.Content = PinIcon(state.Settings.Topmost);
            pinButton.Foreground = state.Settings.Topmost ? accent : muted;
            pinButton.ToolTip = state.Settings.Topmost ? "取消置顶，回到桌面层" : "临时浮在其他窗口上方";
            AutomationProperties.SetName(pinButton, state.Settings.Topmost ? "取消始终置顶" : "始终置顶");
            menuButton.Foreground = muted;
            addButton.Foreground = muted;
            feedbackText.Foreground = muted;
            undoButton.Foreground = accent;
            RefreshView();
        }

        public string DesktopStatus { get { return desktopLayer == null ? "正在连接桌面" : desktopLayer.Status; } }

        public string DesktopDiagnostics { get { return desktopLayer == null ? "not created" : desktopLayer.Describe(); } }

        public bool IsAttachedToDesktop { get { return desktopLayer != null && desktopLayer.IsAttached; } }

        public void ReleaseDesktopLayer() { if (desktopLayer != null) desktopLayer.Dispose(); }

        private static bool SystemIsDark()
        {
            try
            {
                object value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return value is int && (int)value == 0;
            }
            catch { return false; }
        }

        public void RefreshView()
        {
            if (dragging) { refreshAfterDrag = true; return; }
            if (state.Categories == null || state.Items == null) return;
            if (!state.Categories.Any(c => c.Id == state.Settings.ActiveCategoryId))
                state.Settings.ActiveCategoryId = state.Categories.Count > 0 ? state.Categories[0].Id : "inbox";
            double scroll = listScroll.VerticalOffset;
            categoryPanel.Children.Clear();
            categoryUrgency.Clear();
            foreach (TodoCategory category in state.Categories)
            {
                TodoCategory captured = category;
                StackPanel label = new StackPanel { Orientation = Orientation.Horizontal };
                TextBlock name = new TextBlock
                {
                    Text = category.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 156,
                    Foreground = category.Id == state.Settings.ActiveCategoryId ? ink : muted,
                    FontWeight = category.Id == state.Settings.ActiveCategoryId ? FontWeights.SemiBold : FontWeights.Normal,
                    TextDecorations = category.Id == state.Settings.ActiveCategoryId ? TextDecorations.Underline : null
                };
                TextBlock dot = new TextBlock { Margin = new Thickness(3, 0, 0, 0), FontWeight = FontWeights.Medium };
                categoryUrgency[category.Id] = dot;
                label.Children.Add(name);
                label.Children.Add(dot);
                Button b = TextButton(null, "分类：" + category.Name, delegate { SwitchCategory(captured.Id); });
                b.Content = label;
                b.FontSize = 14;
                b.Margin = new Thickness(0, 0, 14, 3);
                b.ToolTip = category.Name + "（右键可管理分类）";
                b.ContextMenu = CategoryMenu(category);
                categoryPanel.Children.Add(b);
            }
            Button addCategory = TextButton("＋", "新增分类", AddCategory);
            addCategory.FontSize = 18;
            addCategory.Width = 28;
            addCategory.HorizontalContentAlignment = HorizontalAlignment.Center;
            addCategory.Foreground = muted;
            categoryPanel.Children.Add(addCategory);

            taskPanel.Children.Clear();
            countdowns.Clear();
            taskRows.Clear();
            pendingRows.Clear();
            List<TodoItem> visible = state.Items.Where(i => string.IsNullOrEmpty(i.DeletedAt) && i.CategoryId == state.Settings.ActiveCategoryId).ToList();
            // The JSON item order is the user's order. Deadlines continue to
            // drive the cross-category reminder, never overwrite manual order.
            List<TodoItem> pending = visible.Where(i => string.IsNullOrEmpty(i.CompletedAt)).ToList();
            List<TodoItem> done = visible.Where(i => !string.IsNullOrEmpty(i.CompletedAt)).OrderByDescending(i => i.CompletedAt).ToList();
            if (pending.Count == 0)
            {
                taskPanel.Children.Add(new TextBlock
                {
                    Text = done.Count > 0 ? "这里的待办都完成了。" : "记下一件想做的事。",
                    FontSize = 14,
                    Foreground = muted,
                    Margin = new Thickness(0, 12, 0, 9),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            foreach (TodoItem item in pending) taskPanel.Children.Add(CreateRow(item));
            if (done.Count > 0)
            {
                Button completed = TextButton((state.Settings.ShowCompleted ? "▾ 已完成 " : "▸ 已完成 ") + done.Count, "展开或收起已完成事项", delegate
                {
                    Commit(delegate { state.Settings.ShowCompleted = !state.Settings.ShowCompleted; }, false);
                });
                completed.Margin = new Thickness(0, 12, 0, 2);
                completed.FontSize = 13;
                completed.Foreground = muted;
                taskPanel.Children.Add(completed);
                if (state.Settings.ShowCompleted)
                    foreach (TodoItem item in done) taskPanel.Children.Add(CreateRow(item));
            }
            listScroll.ScrollToVerticalOffset(scroll);
            UpdateClock(false);
        }

        private UIElement CreateRow(TodoItem item)
        {
            bool done = !string.IsNullOrEmpty(item.CompletedAt);
            Grid host = new Grid { Margin = new Thickness(0, 3, 0, 3), Background = Brushes.Transparent };
            Grid row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            CheckBox check = new CheckBox
            {
                IsChecked = done,
                IsThreeState = false,
                Width = 26,
                Height = 26,
                Template = CreateTodoCheckTemplate(dark),
                FocusVisualStyle = null,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(-2, 0, 0, 0),
                Foreground = ink,
                Cursor = Cursors.Hand,
                ToolTip = done ? "移回待办" : "标记完成"
            };
            AutomationProperties.SetName(check, (done ? "取消完成：" : "完成：") + item.Title);
            check.Click += delegate
            {
                if (Commit(delegate { item.CompletedAt = check.IsChecked == true ? DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture) : null; }, false))
                    ShowFeedback(check.IsChecked == true ? "拿下一件。" : "已移回待办。", false);
            };
            row.Children.Add(check);

            StackPanel content = new StackPanel();
            TextBlock title = new TextBlock
            {
                Text = item.Title,
                Foreground = done ? muted : ink,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 24,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextDecorations = done ? TextDecorations.Strikethrough : null,
                Padding = new Thickness(0, 0, 2, 0)
            };
            AutomationProperties.SetName(title, item.Title);
            title.ToolTip = "双击编辑";
            title.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2) { EditTodo(item); e.Handled = true; }
            };
            content.Children.Add(title);
            if (!done && !string.IsNullOrEmpty(item.DueAt))
            {
                TextBlock countdown = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
                countdowns[item.Id] = countdown;
                Button due = TextButton(null, "编辑截止时间：" + item.Title, delegate { EditTodo(item); });
                due.Content = countdown;
                due.FontSize = 13;
                due.MinHeight = 22;
                due.Padding = new Thickness(0, 3, 0, 0);
                due.HorizontalAlignment = HorizontalAlignment.Left;
                DateTimeOffset? deadline = Deadline.Parse(item.DueAt);
                if (deadline.HasValue) due.ToolTip = deadline.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                content.Children.Add(due);
            }
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            if (!done)
            {
                Button handle = TextButton(null, "拖动调整顺序：" + item.Title, null);
                handle.Width = 24;
                handle.Height = 28;
                handle.Padding = new Thickness(2, 2, 2, 2);
                handle.VerticalAlignment = VerticalAlignment.Top;
                handle.Cursor = Cursors.SizeAll;
                handle.ToolTip = "拖动调整顺序";
                GeometryGroup dots = new GeometryGroup();
                foreach (double x in new double[] { 3, 7 })
                    foreach (double y in new double[] { 4, 8, 12 })
                        dots.Children.Add(new EllipseGeometry(new Point(x, y), .8, .8));
                handle.Content = new System.Windows.Shapes.Path
                {
                    Data = dots, Fill = muted, Width = 12, Height = 18, Opacity = .65,
                    Stretch = Stretch.Uniform,
                    IsHitTestVisible = false
                };
                handle.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
                {
                    pressedDragHandle = handle;
                    dragStart = e.GetPosition(this);
                };
                handle.PreviewMouseLeftButtonUp += delegate { pressedDragHandle = null; };
                handle.PreviewMouseMove += delegate(object sender, MouseEventArgs e) { StartItemDrag(handle, item, e); };
                Grid.SetColumn(handle, 2);
                row.Children.Add(handle);
            }
            Button more = TextButton("⋯", "更多操作：" + item.Title, null);
            more.FontSize = 20;
            more.Width = 28;
            more.Height = 28;
            more.HorizontalContentAlignment = HorizontalAlignment.Center;
            more.Padding = new Thickness(2, 0, 2, 0);
            more.Foreground = muted;
            more.VerticalAlignment = VerticalAlignment.Top;
            ContextMenu actions = Menu();
            AddMenuItem(actions, "编辑事项 / 截止时间", delegate { EditTodo(item); });
            if (state.Categories.Count > 1)
            {
                MenuItem move = new MenuItem { Header = "移到分类" };
                foreach (TodoCategory category in state.Categories.Where(c => c.Id != item.CategoryId))
                {
                    TodoCategory target = category;
                    MenuItem entry = new MenuItem { Header = category.Name };
                    entry.Click += delegate { Commit(delegate { item.CategoryId = target.Id; }, false); };
                    move.Items.Add(entry);
                }
                actions.Items.Add(move);
            }
            actions.Items.Add(new Separator());
            AddMenuItem(actions, "删除事项", delegate { DeleteTodo(item); });
            more.ContextMenu = actions;
            more.Click += delegate { actions.PlacementTarget = more; actions.IsOpen = true; };
            Grid.SetColumn(more, 3);
            row.Children.Add(more);
            row.ContextMenu = actions;
            host.Children.Add(row);
            taskRows[item.Id] = host;
            if (!done)
            {
                var marker = new Border
                {
                    Height = 2, Background = accent, CornerRadius = new CornerRadius(1),
                    VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed,
                    IsHitTestVisible = false
                };
                host.Children.Add(marker);
                pendingRows.Add(new PendingRow { Id = item.Id, Element = host, Marker = marker });
            }
            return host;
        }

        private sealed class PendingRow
        {
            public string Id;
            public FrameworkElement Element;
            public Border Marker;
        }

        private void StartItemDrag(Button handle, TodoItem item, MouseEventArgs e)
        {
            if (dragging || pressedDragHandle != handle) return;
            if (e.LeftButton != MouseButtonState.Pressed) { pressedDragHandle = null; return; }
            Point position = e.GetPosition(this);
            if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            pressedDragHandle = null;
            if (!IsPendingInCategory(item, state.Settings.ActiveCategoryId)) return;
            handle.ReleaseMouseCapture();
            dragging = true;
            dragItemId = item.Id;
            dragCategoryId = item.CategoryId;
            dragToken = Guid.NewGuid().ToString("N");
            dropTargetId = null;
            lastDragScroll = DateTime.MinValue;
            object previousAddContent = addButton.Content;
            addButton.Content = "拖动排序 · 松开保存";
            addButton.Foreground = accent;
            DragDropEffects effect = DragDropEffects.None;
            string target = null;
            bool after = false;
            string category = dragCategoryId;
            string movingId = dragItemId;
            try
            {
                var data = new DataObject();
                data.SetData(TodoDragFormat, dragToken, false);
                effect = DragDrop.DoDragDrop(handle, data, DragDropEffects.Move);
                target = dropTargetId;
                after = dropAfter;
            }
            finally
            {
                dragging = false;
                dragToken = dragItemId = dragCategoryId = dropTargetId = null;
                ClearDropMarker();
                addButton.Content = previousAddContent;
                addButton.Foreground = muted;
                if (refreshAfterDrag) { refreshAfterDrag = false; RefreshView(); }
            }
            // Persist only after the native drag loop releases capture. This
            // also keeps a possible save-error dialog outside the OLE drop call.
            if (effect == DragDropEffects.Move && target != null && state.Settings.ActiveCategoryId == category)
                Commit(delegate { return TodoOrder.MovePending(state, category, movingId, target, after); }, false);
        }

        private static bool IsPendingInCategory(TodoItem item, string category)
        {
            return item != null && item.CategoryId == category &&
                string.IsNullOrEmpty(item.CompletedAt) && string.IsNullOrEmpty(item.DeletedAt);
        }

        private bool IsOwnPendingDrag(DragEventArgs e)
        {
            if (!dragging || dragToken == null || state.Settings.ActiveCategoryId != dragCategoryId ||
                (e.AllowedEffects & DragDropEffects.Move) == 0) return false;
            TodoItem moving = state.Items.FirstOrDefault(i => i.Id == dragItemId);
            if (!IsPendingInCategory(moving, dragCategoryId)) return false;
            try
            {
                return e.Data.GetDataPresent(TodoDragFormat, false) &&
                    string.Equals(e.Data.GetData(TodoDragFormat, false) as string, dragToken, StringComparison.Ordinal);
            }
            catch (ExternalException) { return false; }
        }

        private PendingRow FindDropTarget(DragEventArgs e, out bool after)
        {
            after = false;
            if (!IsOwnPendingDrag(e)) return null;
            double y = e.GetPosition(taskPanel).Y;
            PendingRow last = null;
            double lastBottom = 0;
            foreach (PendingRow candidate in pendingRows)
            {
                double top = candidate.Element.TranslatePoint(new Point(0, 0), taskPanel).Y;
                double height = candidate.Element.ActualHeight;
                if (y <= top + height / 2) return candidate;
                last = candidate;
                lastBottom = top + height;
            }
            // The completed section is not a sorting target.
            if (last != null && y <= lastBottom + 8) { after = true; return last; }
            return null;
        }

        private void DragOverList(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = DragDropEffects.None;
            if (!IsOwnPendingDrag(e)) { ClearDropMarker(); return; }
            DateTime now = DateTime.UtcNow;
            Point point = e.GetPosition(listScroll);
            if ((now - lastDragScroll).TotalMilliseconds >= 80)
            {
                if (point.Y < 24 && listScroll.VerticalOffset > 0)
                {
                    listScroll.ScrollToVerticalOffset(Math.Max(0, listScroll.VerticalOffset - 16));
                    lastDragScroll = now;
                }
                else if (point.Y > listScroll.ActualHeight - 24 && listScroll.VerticalOffset < listScroll.ScrollableHeight)
                {
                    listScroll.ScrollToVerticalOffset(Math.Min(listScroll.ScrollableHeight, listScroll.VerticalOffset + 16));
                    lastDragScroll = now;
                }
            }
            bool after;
            PendingRow target = FindDropTarget(e, out after);
            ClearDropMarker();
            if (target == null || target.Id == dragItemId) return;
            dropMarker = target.Marker;
            dropMarker.VerticalAlignment = after ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            dropMarker.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Move;
        }

        private void DropOnList(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = DragDropEffects.None;
            bool after;
            PendingRow target = FindDropTarget(e, out after);
            ClearDropMarker();
            if (target == null || target.Id == dragItemId) return;
            TodoItem targetItem = state.Items.FirstOrDefault(i => i.Id == target.Id);
            if (!IsPendingInCategory(targetItem, dragCategoryId)) return;
            dropTargetId = target.Id;
            dropAfter = after;
            e.Effects = DragDropEffects.Move;
        }

        private void ClearDropMarker()
        {
            if (dropMarker != null) dropMarker.Visibility = Visibility.Collapsed;
            dropMarker = null;
        }

        private void Tick(object sender, EventArgs e)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            if (feedbackPanel.Visibility == Visibility.Visible && now >= feedbackUntil)
            {
                feedbackPanel.Visibility = Visibility.Collapsed;
                undoItemId = null;
            }
            if (Math.Abs((now - lastTick).TotalSeconds) >= 30) UpdateClock(true);
        }

        private void UpdateClock(bool animate)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            lastTick = now;
            foreach (KeyValuePair<string, TextBlock> entry in countdowns)
            {
                TodoItem item = state.Items.FirstOrDefault(i => i.Id == entry.Key);
                if (item == null) continue;
                int stage = Deadline.Stage(item, now);
                entry.Value.Text = Deadline.Format(item, now);
                entry.Value.Foreground = stage >= 3 ? overdue : stage >= 2 ? urgent : muted;
            }
            List<TodoItem> pending = state.Items.Where(i => string.IsNullOrEmpty(i.CompletedAt) && string.IsNullOrEmpty(i.DeletedAt)).ToList();
            foreach (KeyValuePair<string, TextBlock> entry in categoryUrgency)
            {
                List<TodoItem> categoryItems = pending.Where(i => i.CategoryId == entry.Key && Deadline.Stage(i, now) >= 2).ToList();
                entry.Value.Text = categoryItems.Count > 0 ? "·" : "";
                entry.Value.Foreground = categoryItems.Any(i => Deadline.Stage(i, now) >= 3) ? overdue : urgent;
                entry.Value.ToolTip = categoryItems.Count > 0 ? categoryItems.Count + " 件紧急事项" : null;
            }
            TodoItem nearest = pending.Where(i => Deadline.Parse(i.DueAt).HasValue)
                .OrderBy(i => Deadline.Parse(i.DueAt).Value).FirstOrDefault();
            int currentStage = nearest == null ? 0 : Deadline.Stage(nearest, now);
            reminderButton.Visibility = currentStage == 0 ? Visibility.Collapsed : Visibility.Visible;
            reminderItemId = nearest == null ? null : nearest.Id;
            if (currentStage > 0 && nearest != null)
            {
                reminderFace.Text = Deadline.Face(currentStage);
                string taskName = nearest.Title.Length > 8 ? nearest.Title.Substring(0, 8) + "…" : nearest.Title;
                TodoCategory category = state.Categories.FirstOrDefault(c => c.Id == nearest.CategoryId);
                string prefix = nearest.CategoryId != state.Settings.ActiveCategoryId && category != null ? category.Name + " · " : "";
                string nudge = currentStage >= 3 ? "到站了，重新安排？" : currentStage >= 2 ? "我开始坐不住了。" : "先动一小步？";
                reminderText.Text = prefix + taskName + "：" + nudge;
                reminderButton.ToolTip = "点击查看：" + nearest.Title + "\n" + Deadline.Format(nearest, now) + "\n" + Deadline.Mood(currentStage);
                reminderText.Foreground = currentStage >= 3 ? overdue : currentStage >= 2 ? urgent : muted;
                reminderFace.Foreground = reminderText.Foreground;
                AutomationProperties.SetName(reminderButton, "紧急事项：" + nearest.Title + "，" + Deadline.Format(nearest, now));
                if (animate && lastReminderStage >= 0 && currentStage > lastReminderStage && state.Settings.Animations && IsVisible)
                {
                    DoubleAnimationUsingKeyFrames motion = new DoubleAnimationUsingKeyFrames();
                    motion.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    motion.KeyFrames.Add(new LinearDoubleKeyFrame(-2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
                    motion.KeyFrames.Add(new LinearDoubleKeyFrame(2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
                    motion.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320))));
                    reminderFace.RenderTransform.BeginAnimation(TranslateTransform.XProperty, motion);
                }
            }
            lastReminderStage = currentStage;
        }

        private void GoToReminder()
        {
            TodoItem item = state.Items.FirstOrDefault(i => i.Id == reminderItemId);
            if (item == null) return;
            if (state.Settings.ActiveCategoryId != item.CategoryId) SwitchCategory(item.CategoryId);
            Dispatcher.BeginInvoke(new Action(delegate
            {
                FrameworkElement row;
                if (taskRows.TryGetValue(item.Id, out row)) row.BringIntoView();
            }), DispatcherPriority.Loaded);
        }

        private void SwitchCategory(string id)
        {
            if (state.Settings.ActiveCategoryId == id) return;
            if (Commit(delegate { state.Settings.ActiveCategoryId = id; }, false)) listScroll.ScrollToTop();
        }

        private void AddTodo()
        {
            TodoCategory category = state.Categories.FirstOrDefault(c => c.Id == state.Settings.ActiveCategoryId);
            TodoEditResult result = TodoDialogs.EditTodo(this, null, category == null ? DefaultCategoryName : category.Name);
            if (result == null) return;
            if (Commit(delegate
            {
                state.Items.Add(new TodoItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CategoryId = state.Settings.ActiveCategoryId,
                    Title = result.Title,
                    DueAt = result.DueAt,
                    CreatedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture)
                });
            }, false)) ShowFeedback("记下了，一件一件来。", false);
        }

        private void EditTodo(TodoItem item)
        {
            TodoCategory category = state.Categories.FirstOrDefault(c => c.Id == item.CategoryId);
            TodoEditResult result = TodoDialogs.EditTodo(this, item, category == null ? DefaultCategoryName : category.Name);
            if (result == null) return;
            Commit(delegate { item.Title = result.Title; item.DueAt = result.DueAt; }, false);
        }

        private void DeleteTodo(TodoItem item)
        {
            string oldDeletedAt = item.DeletedAt;
            if (Commit(delegate { item.DeletedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture); }, false))
            {
                undoItemId = item.Id;
                undoOldDeletedAt = oldDeletedAt;
                ShowFeedback("事项已删除。", true);
            }
        }

        private void UndoDelete()
        {
            TodoItem item = state.Items.FirstOrDefault(i => i.Id == undoItemId);
            if (item == null || DateTimeOffset.Now >= feedbackUntil) { feedbackPanel.Visibility = Visibility.Collapsed; return; }
            if (Commit(delegate { item.DeletedAt = undoOldDeletedAt; state.Settings.ActiveCategoryId = item.CategoryId; }, false))
            {
                undoItemId = null;
                ShowFeedback("已恢复。", false);
            }
        }

        private void ShowFeedback(string message, bool undo)
        {
            feedbackText.Text = message;
            undoButton.Visibility = undo ? Visibility.Visible : Visibility.Collapsed;
            feedbackPanel.Visibility = Visibility.Visible;
            feedbackUntil = DateTimeOffset.Now.AddSeconds(undo ? 10 : 4);
            if (!undo) undoItemId = null;
        }

        private void AddCategory()
        {
            string name = TodoDialogs.PromptName(this, "新增分类", "");
            if (name == null) return;
            if (state.Categories.Any(c => string.Equals(c.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            {
                TodoDialogs.Notice(this, "分类名称重复", "已经有这个分类了，可以换一个名字。");
                return;
            }
            Commit(delegate
            {
                TodoCategory category = new TodoCategory { Id = Guid.NewGuid().ToString("N"), Name = name };
                state.Categories.Add(category);
                state.Settings.ActiveCategoryId = category.Id;
            }, false);
        }

        private void RenameCategory(TodoCategory category)
        {
            string name = TodoDialogs.PromptName(this, "重命名分类", category.Name);
            if (name == null || name == category.Name) return;
            if (state.Categories.Any(c => c.Id != category.Id && string.Equals(c.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            {
                TodoDialogs.Notice(this, "分类名称重复", "已经有这个分类了，可以换一个名字。");
                return;
            }
            Commit(delegate { category.Name = name; }, false);
        }

        private void DeleteCategory(TodoCategory category)
        {
            if (category.Id == "inbox") return;
            if (!TodoDialogs.Confirm(this, "删除分类", "删除“" + category.Name + "”分类？其中的事项会移到“" + DefaultCategoryName + "”。")) return;
            Commit(delegate
            {
                foreach (TodoItem item in state.Items.Where(i => i.CategoryId == category.Id)) item.CategoryId = "inbox";
                state.Categories.Remove(category);
                if (state.Settings.ActiveCategoryId == category.Id) state.Settings.ActiveCategoryId = "inbox";
            }, false);
        }

        private string DefaultCategoryName
        {
            get
            {
                TodoCategory category = state.Categories.FirstOrDefault(c => c.Id == "inbox");
                return category == null ? "默认" : category.Name;
            }
        }

        private ContextMenu Menu()
        {
            return MenuStyles.Create(dark);
        }

        private static MenuItem AddMenuItem(ItemsControl menu, string title, Action action)
        {
            MenuItem entry = new MenuItem { Header = title };
            entry.Click += delegate { action(); };
            menu.Items.Add(entry);
            return entry;
        }

        private ContextMenu CategoryMenu(TodoCategory category)
        {
            ContextMenu menu = Menu();
            AddMenuItem(menu, "重命名分类", delegate { RenameCategory(category); });
            if (category.Id != "inbox") AddMenuItem(menu, "删除分类", delegate { DeleteCategory(category); });
            return menu;
        }

        private void OpenMainMenu()
        {
            ContextMenu menu = Menu();
            AddMenuItem(menu, "添加事项", AddTodo).InputGestureText = "Ctrl+N";
            AddMenuItem(menu, "新增分类", AddCategory);
            MenuItem manage = new MenuItem { Header = "管理分类" };
            foreach (TodoCategory category in state.Categories)
            {
                TodoCategory captured = category;
                MenuItem sub = new MenuItem { Header = category.Name };
                AddMenuItem(sub, "重命名", delegate { RenameCategory(captured); });
                if (category.Id != "inbox") AddMenuItem(sub, "删除", delegate { DeleteCategory(captured); });
                manage.Items.Add(sub);
            }
            manage.IsEnabled = manage.Items.Count > 0;
            menu.Items.Add(manage);
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "外观与设置", ShowSettings);
            AddMenuItem(menu, "打开数据文件夹", delegate { if (OpenDataRequested != null) OpenDataRequested(this, EventArgs.Empty); });
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "隐藏到托盘", delegate { Hide(); });
            AddMenuItem(menu, "退出", delegate { if (ExitRequested != null) ExitRequested(this, EventArgs.Empty); });
            menu.PlacementTarget = menuButton;
            menuButton.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void ToggleTopmost()
        {
            Commit(delegate { state.Settings.Topmost = !state.Settings.Topmost; }, true);
        }

        public void ShowSettings()
        {
            SettingsEditResult result = TodoDialogs.EditSettings(this, state.Settings);
            if (result == null) return;
            Commit(delegate
            {
                state.Settings.BackgroundOpacity = result.BackgroundOpacity;
                state.Settings.Theme = result.Theme;
                state.Settings.Animations = result.Animations;
                state.Settings.StartWithWindows = result.StartWithWindows;
            }, true);
        }

        private bool Commit(Action change, bool settingsChanged)
        {
            return Commit(delegate { change(); return true; }, settingsChanged);
        }

        private bool Commit(Func<bool> change, bool settingsChanged)
        {
            TodoState before = CopyState();
            try
            {
                if (!change()) return false;
                persist();
            }
            catch (Exception ex)
            {
                RestoreState(before);
                ApplySettings();
                TodoDialogs.Notice(this, "保存失败", "这次修改没有保存，已恢复修改前的内容。\n\n" + ex.Message);
                return false;
            }
            if (settingsChanged)
            {
                ApplySettings();
                if (SettingsChanged != null) SettingsChanged(this, EventArgs.Empty);
            }
            else RefreshView();
            return true;
        }

        private TodoState CopyState()
        {
            return new TodoState
            {
                Version = state.Version,
                Categories = state.Categories.Select(c => new TodoCategory { Id = c.Id, Name = c.Name }).ToList(),
                Items = state.Items.Select(i => new TodoItem
                {
                    Id = i.Id, CategoryId = i.CategoryId, Title = i.Title, CreatedAt = i.CreatedAt,
                    DueAt = i.DueAt, CompletedAt = i.CompletedAt, DeletedAt = i.DeletedAt
                }).ToList(),
                Settings = new AppSettings
                {
                    BackgroundOpacity = state.Settings.BackgroundOpacity,
                    Topmost = state.Settings.Topmost,
                    Animations = state.Settings.Animations,
                    StartWithWindows = state.Settings.StartWithWindows,
                    Theme = state.Settings.Theme,
                    ActiveCategoryId = state.Settings.ActiveCategoryId,
                    ShowCompleted = state.Settings.ShowCompleted
                }
            };
        }

        private void RestoreState(TodoState before)
        {
            state.Version = before.Version;
            state.Categories = before.Categories;
            state.Items = before.Items;
            state.Settings = before.Settings;
        }

        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_DISPLAYCHANGE = 0x007E;
            const int WM_DPICHANGED = 0x02E0;
            const int WM_SETTINGCHANGE = 0x001A;
            const int WM_POWERBROADCAST = 0x0218;
            if (message == WM_DPICHANGED || message == WM_DISPLAYCHANGE)
                Dispatcher.BeginInvoke(new Action(PositionAtTopRight), DispatcherPriority.Loaded);
            if (message == WM_SETTINGCHANGE)
                Dispatcher.BeginInvoke(new Action(delegate { ApplySettings(); PositionAtTopRight(); }), DispatcherPriority.Background);
            if (message == WM_POWERBROADCAST)
                Dispatcher.BeginInvoke(new Action(delegate { UpdateClock(false); }), DispatcherPriority.Background);
            return IntPtr.Zero;
        }

        private void PositionAtTopRight()
        {
            if (locating) return;
            locating = true;
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                IntPtr monitor = MonitorFromWindow(hwnd, 2);
                MonitorInfo info = new MonitorInfo();
                info.Size = Marshal.SizeOf(typeof(MonitorInfo));
                if (!GetMonitorInfo(monitor, ref info))
                {
                    Left = SystemParameters.WorkArea.Right - ActualWidth - 20;
                    Top = SystemParameters.WorkArea.Top + 20;
                    return;
                }
                double scale = 1.0;
                HwndSource source = HwndSource.FromHwnd(hwnd);
                if (source != null && source.CompositionTarget != null) scale = source.CompositionTarget.TransformToDevice.M11;
                if (scale <= 0) scale = 1;
                double allowedHeight = Math.Max(160, (info.Work.Bottom - info.Work.Top) / scale - 40);
                MaxHeight = Math.Min(600, allowedHeight);
                listScroll.MaxHeight = Math.Max(100, MaxHeight - 190);
                int widthPixels = (int)Math.Round((ActualWidth > 0 ? ActualWidth : Width) * scale);
                int margin = (int)Math.Round(20 * scale);
                if (desktopLayer != null) desktopLayer.Position(info.Work.Right - widthPixels - margin, info.Work.Top + margin);
                else SetWindowPos(hwnd, IntPtr.Zero, info.Work.Right - widthPixels - margin, info.Work.Top + margin, 0, 0, 0x0015);
            }
            finally { locating = false; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
