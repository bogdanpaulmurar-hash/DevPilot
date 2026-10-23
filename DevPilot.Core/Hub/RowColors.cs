using System.Globalization;
using DevPilot.Models;

namespace DevPilot.Core.Hub;

/// <summary>An opaque colour.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// Colour rules for hub rows: marker / tint visibility, the tint colour, and readability checks so a chosen
/// text or symbol colour never disappears on the panel background (dark or light theme).
/// </summary>
public static class RowColors
{
    /// <summary>Minimum WCAG contrast for a custom text or symbol colour; below it the theme colour is used.</summary>
    public const double MinContrast = 3.0;

    public const int MinStrength = 5;
    public const int MaxStrength = 60;

    /// <summary>Parses "#RRGGBB" or "#AARRGGBB" (alpha ignored).</summary>
    public static bool TryParse(string? hex, out Rgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 8) s = s[2..];
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static bool ShowsMarker(RowColorStyle style, string? color) =>
        TryParse(color, out _) && style is RowColorStyle.Marker or RowColorStyle.MarkerAndHighlight;

    public static bool ShowsHighlight(RowColorStyle style, string? color) =>
        TryParse(color, out _) && style is RowColorStyle.Highlight or RowColorStyle.MarkerAndHighlight;

    /// <summary>The tint as "#AARRGGBB" (strength in percent becomes the alpha), or empty when there is no colour.</summary>
    public static string HighlightHex(string? color, int strength)
    {
        if (!TryParse(color, out var c)) return string.Empty;
        var a = (byte)Math.Round(Math.Clamp(strength, MinStrength, MaxStrength) * 255 / 100.0);
        return $"#{a:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    /// <summary><paramref name="fore"/> drawn over <paramref name="back"/> with the given opacity (0 – 1).</summary>
    public static Rgb Blend(Rgb back, Rgb fore, double alpha)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        byte Mix(byte b, byte f) => (byte)Math.Round(b + (f - b) * alpha);
        return new Rgb(Mix(back.R, fore.R), Mix(back.G, fore.G), Mix(back.B, fore.B));
    }

    public static double Luminance(Rgb c)
    {
        static double Ch(byte v) { var d = v / 255.0; return d <= 0.03928 ? d / 12.92 : Math.Pow((d + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    /// <summary>WCAG contrast ratio, 1 – 21.</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// The background a row actually shows: the panel surface, tinted when the row is highlighted.
    /// </summary>
    public static Rgb Background(string surface, string? highlightColor, int strength)
    {
        var bg = TryParse(surface, out var s) ? s : new Rgb(0x1F, 0x21, 0x25);
        if (TryParse(highlightColor, out var h))
            bg = Blend(bg, h, Math.Clamp(strength, MinStrength, MaxStrength) / 100.0);
        return bg;
    }

    /// <summary>
    /// A custom text or symbol colour if it stays readable on the row, otherwise empty (meaning: use the theme colour).
    /// </summary>
    public static string Readable(string? wanted, string surface, string? highlightColor, int strength)
    {
        if (!TryParse(wanted, out var w)) return string.Empty;
        return Contrast(w, Background(surface, highlightColor, strength)) >= MinContrast ? w.Hex : string.Empty;
    }
}
