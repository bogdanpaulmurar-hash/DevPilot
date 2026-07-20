// ============================================================
// DevPilot — IT Automation Hub v1.0
// BoolToAccentBrushConverter.cs
// true  → AccentBrush (teal #009999) — buton activ
// false → FgMutedBrush (gri)         — buton inactiv
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DevPilot.UI.Converters;

public class BoolToAccentBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true)
            return Application.Current.Resources["AccentBrush"] as Brush
                   ?? new SolidColorBrush(Color.FromRgb(0, 153, 153));

        return Application.Current.Resources["FgMutedBrush"] as Brush
               ?? new SolidColorBrush(Color.FromRgb(120, 120, 120));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}