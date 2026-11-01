using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTodo;

internal static class ScaleLayoutTests
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var board = new DrawingVisual();
            using (DrawingContext drawing = board.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, 1390, 960));
                double x = 14;
                foreach (double scale in new[] { .8, 1.0, 1.5 })
                {
                    BitmapSource preview = CheckLayout(scale, 1920, 1080, false);
                    drawing.DrawImage(preview, new Rect(x, 18, preview.Width, preview.Height));
                    x += preview.Width + 18;
                    CheckLayout(scale, 800, 600, true);
                    CheckLayout(scale, 640, 360, true);
                }
            }
            if (args.Length > 0)
            {
                var bitmap = new RenderTargetBitmap(1390, 960, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(board);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(args[0])) encoder.Save(stream);
            }
            Console.WriteLine("PASS 9 real main-window layouts: 80/100/150%, proportional text and hit targets, short-screen footer and scrolling; no window shown.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static BitmapSource CheckLayout(double scale, double workWidth, double workHeight, bool crowded)
    {
        var state = new TodoState();
        state.Settings.UiScale = scale;
        state.Settings.BackgroundOpacity = .95;
        state.Settings.Theme = crowded ? "dark" : "light";
        state.Categories.Add(new TodoCategory { Id = "inbox", Name = "默认" });
        state.Categories.Add(new TodoCategory { Id = "study", Name = "课程学习" });
        state.Categories.Add(new TodoCategory { Id = "project", Name = "长期项目" });
        if (crowded)
            for (int i = 0; i < 12; i++) state.Categories.Add(new TodoCategory { Id = "more-" + i, Name = "多分类布局检查 " + i });
        for (int i = 0; i < (crowded ? 24 : 3); i++)
            state.Items.Add(new TodoItem { Id = "scale-" + i, CategoryId = "inbox",
                Title = i == 0 ? "整理阅读笔记" : "完善课程项目方案，整理实验结果与下一步计划",
                CreatedAt = DateTimeOffset.Now.ToString("o"), DueAt = DateTimeOffset.Now.AddHours(crowded ? 1 : 72).ToString("o") });
        var window = new MainWindow(state, delegate { throw new Exception("Layout preview must not save data."); });
        try
        {
            typeof(MainWindow).GetMethod("ApplyScale", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[] { workWidth, workHeight });
            if (crowded) typeof(MainWindow).GetMethod("ShowFeedback", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[] { "事项已删除。", true });
            var host = (FrameworkElement)window.Content;
            host.Measure(new Size(window.Width, window.MaxHeight));
            double height = Math.Min(window.MaxHeight, host.DesiredSize.Height);
            host.Arrange(new Rect(0, 0, window.Width, height)); host.UpdateLayout();
            var panel = Field<StackPanel>(window, "taskPanel");
            var check = Find<CheckBox>(panel);
            var add = Field<Button>(window, "addButton");
            Rect checkBounds = check.TransformToAncestor(host).TransformBounds(new Rect(check.RenderSize));
            Rect addBounds = add.TransformToAncestor(host).TransformBounds(new Rect(add.RenderSize));
            double effectiveScale = window.Width / 400;
            if (effectiveScale > scale + .001 || window.Width > workWidth - 39 || Math.Abs(checkBounds.Width - 26 * effectiveScale) > 2)
                throw new Exception("Panel and completion hit target must scale together.");
            if (addBounds.Bottom > height + 1 || addBounds.Top < 0 || addBounds.Width < 60 * effectiveScale)
                throw new Exception("Add button became clipped at " + scale + " / height " + workHeight + ": " + addBounds + ", available height " + height);
            if (crowded)
            {
                var undo = Field<Button>(window, "undoButton");
                Rect undoBounds = undo.TransformToAncestor(host).TransformBounds(new Rect(undo.RenderSize));
                if (undoBounds.Bottom > height + 1) throw new Exception("Undo button became clipped.");
            }
            TextBlock title = Find<TextBlock>(panel);
            Point origin = title.TransformToAncestor(host).Transform(new Point(0, 0));
            Point unit = title.TransformToAncestor(host).Transform(new Point(0, 10));
            if (Math.Abs((unit.Y - origin.Y) / 10 - effectiveScale) > .01 || title.FontSize != 16)
                throw new Exception("Text and panel scale diverged.");
            ScrollViewer scroll = Field<ScrollViewer>(window, "listScroll");
            if (crowded && scroll.ScrollableHeight <= 0) throw new Exception("Long list must remain scrollable.");
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.Width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            return bitmap;
        }
        finally { window.ReleaseDesktopLayer(); window.Close(); }
    }

    private static T Field<T>(object owner, string name) where T : class
    { return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }

    private static T Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T) return (T)root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { T child = Find<T>(VisualTreeHelper.GetChild(root, i)); if (child != null) return child; }
        return null;
    }
}
