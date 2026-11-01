using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTodo;

internal static class MenuPreview
{
    private static int checkedTemplates;

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            // Pure layout/rendering: no Window.Show, Popup.IsOpen or HWND creation.
            var canvas = new Canvas { Width = 760, Height = 700, Background = Solid("#C9D4CB") };
            for (int theme = 0; theme != 2; theme++)
            {
                bool dark = theme != 0;
                double top = theme == 0 ? 42 : 382;
                var label = new TextBlock
                {
                    Text = dark ? "深色" : "浅色", FontSize = 13,
                    FontFamily = new FontFamily("Microsoft YaHei UI"), Foreground = Solid("#43564A")
                };
                Canvas.SetLeft(label, 20); Canvas.SetTop(label, top - 25); canvas.Children.Add(label);
                for (int kind = 0; kind < 3; kind++)
                {
                    FrameworkElement sample = Sample(dark, kind);
                    Canvas.SetLeft(sample, 20 + kind * 245); Canvas.SetTop(sample, top);
                    canvas.Children.Add(sample);
                }
            }
            canvas.Measure(new Size(canvas.Width, canvas.Height));
            canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
            canvas.UpdateLayout();
            ValidateTree(canvas);
            var bitmap = new RenderTargetBitmap(1140, 1050, 144, 144, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            string path = args.Length == 0 ? "menu-preview.png" : args[0];
            using (var stream = File.Create(path)) png.Save(stream);
            Console.WriteLine("Validated menu templates: " + checkedTemplates + "; rendered: " + Path.GetFullPath(path));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static FrameworkElement Sample(bool dark, int kind)
    {
        ContextMenu source = MenuStyles.Create(dark);
        source.ApplyTemplate();
        checkedTemplates++;
        var panel = new StackPanel();
        if (kind == 0)
        {
            Add(panel, "隐藏待办");
            MenuItem pin = Add(panel, "始终置顶"); pin.IsCheckable = true; pin.IsChecked = true;
            Add(panel, "外观与设置");
            Add(panel, "打开数据文件夹");
            panel.Children.Add(new Separator());
            Add(panel, "退出");
        }
        else if (kind == 1)
        {
            Add(panel, "添加事项").InputGestureText = "Ctrl+N";
            Add(panel, "新增分类");
            MenuItem manage = Add(panel, "管理分类");
            manage.Items.Add(new MenuItem { Header = "工作" });
            panel.Children.Add(new Separator());
            Add(panel, "外观与设置");
            Add(panel, "打开数据文件夹");
            panel.Children.Add(new Separator());
            Add(panel, "隐藏到托盘");
            Add(panel, "退出");
        }
        else
        {
            Add(panel, "重命名分类");
            Add(panel, "删除（事项移到默认）");
            panel.Children.Add(new Separator());
            Add(panel, "不可用的菜单项").IsEnabled = false;
            MenuItem longItem = Add(panel, "这是一个很长的自定义分类名称，用于检查边界");
            longItem.Items.Add(new MenuItem { Header = "重命名" });
        }
        return new Border
        {
            Width = 220, Padding = new Thickness(4), CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1), Resources = source.Resources,
            Background = (Brush)source.Resources["MenuSurface"], BorderBrush = (Brush)source.Resources["MenuLine"],
            Child = panel, UseLayoutRounding = true
        };
    }

    private static MenuItem Add(Panel panel, string text)
    {
        var item = new MenuItem { Header = text };
        panel.Children.Add(item);
        return item;
    }

    private static void ValidateTree(DependencyObject element)
    {
        var item = element as MenuItem;
        if (item != null)
        {
            item.ApplyTemplate();
            if (!(item.Template.FindName("PART_Popup", item) is Popup))
                throw new Exception("MenuItem lost PART_Popup: " + item.Header);
            if (!(item.Template.FindName("ItemSurface", item) is Border))
                throw new Exception("MenuItem did not use shared template: " + item.Header);
            checkedTemplates++;
        }
        var separator = element as Separator;
        if (separator != null) { separator.ApplyTemplate(); checkedTemplates++; }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            ValidateTree(VisualTreeHelper.GetChild(element, i));
    }

    private static Brush Solid(string color) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
}
