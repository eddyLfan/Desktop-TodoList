using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DesktopTodo;

public static class DialogTemplateSmoke
{
    private static int controls;
    [STAThread]
    public static int Main()
    {
        try
        {
            foreach (string theme in new string[] { "light", "dark" }) TestTheme(theme);
            Console.WriteLine("PASS: offscreen resources and templates, " + controls + " controls; no window shown.");
            return 0;
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            Console.WriteLine(error.ToString());
            return 1;
        }
    }
    private static void TestTheme(string theme)
    {
        Type dialogs = typeof(TodoDialogs);
        object palette = dialogs.GetMethod("Colors", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { null, theme });
        ResourceDictionary resources = (ResourceDictionary)dialogs.GetMethod("Styles", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { palette });
        Console.WriteLine("Loaded " + theme + " resources: " + resources.Count);
        StackPanel root = new StackPanel { Width = 360, Resources = resources };
        List<Control> targets = new List<Control>();
        foreach (DictionaryEntry entry in resources)
        {
            Style style = entry.Value as Style;
            if (style == null) continue;
            Control c = (Control)Activator.CreateInstance(style.TargetType);
            c.Style = style;
            c.Resources = resources;
            ContentControl content = c as ContentControl;
            if (content != null) content.Content = "中文输入检查";
            TextBox text = c as TextBox;
            if (text != null) text.Text = "中文输入检查";
            DatePicker date = c as DatePicker;
            if (date != null) date.SelectedDate = new DateTime(2026, 11, 1);
            Slider slider = c as Slider;
            if (slider != null) { slider.Minimum = 0.1; slider.Maximum = .95; slider.Value = .35; }
            root.Children.Add(c);
            targets.Add(c);
            Console.WriteLine("Prepared " + entry.Key + " -> " + style.TargetType.Name);
        }
        root.Measure(new Size(360, double.PositiveInfinity));
        root.Arrange(new Rect(new Point(), root.DesiredSize));
        foreach (Control target in targets)
        {
            target.ApplyTemplate();
            ExpandTree(target);
            controls++;
            DatePicker date = target as DatePicker;
            if (date != null)
            {
                if (date.Template.FindName("PART_TextBox", date) == null || date.Template.FindName("PART_Button", date) == null || date.Template.FindName("PART_Popup", date) == null)
                    throw new Exception("DatePicker is missing a required template part.");
            }
            Slider slider = target as Slider;
            if (slider != null && slider.Template.FindName("PART_Track", slider) == null) throw new Exception("Slider missing track.");
        }
        root.UpdateLayout();
    }
    private static void ExpandTree(DependencyObject visual)
    {
        Control c = visual as Control;
        if (c != null) c.ApplyTemplate();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++) ExpandTree(VisualTreeHelper.GetChild(visual, i));
    }
}
