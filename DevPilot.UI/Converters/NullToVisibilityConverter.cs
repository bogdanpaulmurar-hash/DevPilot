// ============================================================
// DevPilot — IT Automation Hub v1.0
// NullToVisibilityConverter.cs — null → Collapsed, non-null → Visible
// Phase: 7
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DevPilot.UI.Converters;

[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
