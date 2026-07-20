// ============================================================
// DevPilot — IT Automation Hub v1.0
// PathToImageConverter.cs — Converts file path string to BitmapImage
// Phase: 7
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DevPilot.UI.Converters;

[ValueConversion(typeof(string), typeof(BitmapImage))]
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource       = new Uri(path);
            bmp.CacheOption     = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 800;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;

    // ── BoolToAccentBrushConverter ────────────────────────────────────────────
    // true  → AccentBrush (teal #009999) — buton Font Style activ
    // false → FgSecondaryBrush           — buton inactiv
    public class BoolToAccentBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is true)
                return System.Windows.Application.Current.Resources["AccentBrush"] as Brush
                       ?? new SolidColorBrush(Color.FromRgb(0, 153, 153));

            return System.Windows.Application.Current.Resources["FgSecondaryBrush"] as Brush
                   ?? new SolidColorBrush(Color.FromRgb(160, 160, 160));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
