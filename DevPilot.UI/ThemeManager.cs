// ============================================================
// DevPilot — IT Automation Hub v1.0
// ThemeManager.cs — Switches WPF resource dictionary at runtime
// Phase: 9
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System;
using System.Windows;

namespace DevPilot.UI;

/// <summary>
/// Static helper that swaps DarkTheme.xaml / LightTheme.xaml
/// in Application.Current.Resources at runtime.
/// Must be called on the UI thread.
/// </summary>
public static class ThemeManager
{
    /// <summary>Applies the dark or light theme to the running application.</summary>
    public static void Apply(bool isDark)
    {
        var themeUri = new Uri(
            isDark ? "Styles/DarkTheme.xaml" : "Styles/LightTheme.xaml",
            UriKind.Relative);

        var dicts = Application.Current.Resources.MergedDictionaries;

        for (int i = 0; i < dicts.Count; i++)
        {
            var src = dicts[i].Source?.ToString() ?? string.Empty;
            if (src.Contains("DarkTheme", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("LightTheme", StringComparison.OrdinalIgnoreCase))
            {
                dicts[i] = new ResourceDictionary { Source = themeUri };
                return;
            }
        }

        // No theme entry found yet — insert at position 0
        dicts.Insert(0, new ResourceDictionary { Source = themeUri });
    }
}