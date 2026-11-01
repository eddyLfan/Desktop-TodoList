using System;
using System.Windows;

namespace DesktopTodo
{
    // The selected UI scale is separate from Windows DPI scaling. Popup surfaces
    // read this inherited value and apply it once to their own layout root.
    public static class UiScaling
    {
        public static readonly DependencyProperty ScaleProperty = DependencyProperty.RegisterAttached(
            "Scale", typeof(double), typeof(UiScaling),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.Inherits));

        public static double GetScale(DependencyObject target)
        {
            return target == null ? 1.0 : AppSettings.NormalizeUiScale((double)target.GetValue(ScaleProperty));
        }

        public static void SetScale(DependencyObject target, double value)
        {
            target.SetValue(ScaleProperty, AppSettings.NormalizeUiScale(value));
        }

        public static double FitToWidth(double requested, double baseWidth, double availableWidth)
        {
            return Math.Min(AppSettings.NormalizeUiScale(requested), Math.Max(1, availableWidth) / baseWidth);
        }
    }
}
