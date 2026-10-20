using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DevPilot.Models;

namespace DevPilot.UI.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value switch
        {
            bool x => x,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i != 0,
            null => false,
            _ => true,
        };
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && (v == Visibility.Visible) != Invert;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>"#RRGGBB" → brush; empty → Transparent.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ColorUtil.TryParse(value as string, out var c) ? new SolidColorBrush(c) : Brushes.Transparent;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Compares an enum value with the ConverterParameter (for RadioButtons).</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}

public sealed class RunStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is ToolRunState s ? s switch
        {
            ToolRunState.Running => "StatusRunningBrush",
            ToolRunState.Starting => "StatusStartingBrush",
            ToolRunState.Failed => "StatusFailedBrush",
            ToolRunState.Stopped => "StatusStoppedBrush",
            _ => null,
        } : null;
        return key is null ? Brushes.Transparent : Application.Current.TryFindResource(key) ?? Brushes.Gray;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FontWeights.SemiBold : FontWeights.Normal;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolToFontStyleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FontStyles.Italic : FontStyles.Normal;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public static class ColorUtil
{
    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try
        {
            var obj = ColorConverter.ConvertFromString(hex.Trim());
            if (obj is Color c) { color = c; return true; }
        }
        catch (FormatException) { }
        return false;
    }

    public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    public static double Luminance(Color c)
    {
        static double Ch(byte v) { var d = v / 255.0; return d <= 0.03928 ? d / 12.92 : Math.Pow((d + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    public static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)Math.Clamp(c.R + (255 - c.R) * amount, 0, 255),
        (byte)Math.Clamp(c.G + (255 - c.G) * amount, 0, 255),
        (byte)Math.Clamp(c.B + (255 - c.B) * amount, 0, 255));

    public static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)Math.Clamp(c.R * (1 - amount), 0, 255),
        (byte)Math.Clamp(c.G * (1 - amount), 0, 255),
        (byte)Math.Clamp(c.B * (1 - amount), 0, 255));

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
