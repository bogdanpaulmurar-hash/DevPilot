using System.Windows;
using System.Windows.Media;
using DevPilot.Models;
using DevPilot.UI.Converters;
using Microsoft.Win32;

namespace DevPilot.UI.Services;

/// <summary>Swaps the Dark/Light palette and publishes the single accent colour as brushes.</summary>
public sealed class ThemeService
{
    private ResourceDictionary? _palette;
    private ThemeMode _mode = ThemeMode.Dark;

    public bool IsDark { get; private set; } = true;
    public event EventHandler? ThemeChanged;

    public ThemeService()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _mode == ThemeMode.System)
                Application.Current?.Dispatcher.BeginInvoke(() => ApplyPalette(_mode));
        };
    }

    public void Apply(AppSettings s)
    {
        _mode = s.Theme;
        ApplyPalette(s.Theme);
        ApplyAccent(s.Accent, s.Hub.Spotlight.Accent);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyPalette(ThemeMode mode)
    {
        bool dark = mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.System => !SystemUsesLightTheme(),
            _ => true,
        };
        IsDark = dark;
        var dict = new ResourceDictionary
        {
            Source = new Uri(dark ? "pack://application:,,,/Themes/Dark.xaml" : "pack://application:,,,/Themes/Light.xaml"),
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        // Remove every palette (including the one App.xaml loads), otherwise a later Dark.xaml would win.
        foreach (var old in merged.Where(IsPalette).ToList()) merged.Remove(old);
        merged.Insert(0, dict);
        _palette = dict;
    }

    private static bool IsPalette(ResourceDictionary d)
    {
        var src = d.Source?.OriginalString ?? string.Empty;
        return src.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase) || src.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase);
    }

    public static void ApplyAccent(string accentHex, string spotlightHex)
    {
        if (!ColorUtil.TryParse(accentHex, out var accent)) accent = Color.FromRgb(0x4F, 0x8E, 0xF7);
        var res = Application.Current.Resources;
        res["AccentColor"] = accent;
        res["AccentBrush"] = Frozen(accent);
        res["AccentHoverBrush"] = Frozen(ColorUtil.Lighten(accent, 0.12));
        res["AccentSoftBrush"] = Frozen(ColorUtil.WithAlpha(accent, 0x2E));
        res["AccentForegroundBrush"] = Frozen(ColorUtil.Luminance(accent) > 0.45 ? Colors.Black : Colors.White);

        if (!ColorUtil.TryParse(spotlightHex, out var spot)) spot = accent;
        res["SpotlightBrush"] = Frozen(spot);
        res["SpotlightSoftBrush"] = Frozen(ColorUtil.WithAlpha(spot, 0x24));
        res["SpotlightForegroundBrush"] = Frozen(ColorUtil.Luminance(spot) > 0.45 ? Colors.Black : Colors.White);
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
