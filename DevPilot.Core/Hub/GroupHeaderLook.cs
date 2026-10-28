using DevPilot.Models;

namespace DevPilot.Core.Hub;

/// <summary>How one group's header is drawn: title size, font, weight and logo. Pure values, the hub turns them into WPF.</summary>
public static class GroupHeaderLook
{
    /// <summary>Size of the small-caps header (the hub default), in device-independent pixels.</summary>
    public const double SmallCapsSize = 11;

    public static double TitleSize(GroupTitleSize size, GroupHeaderStyle style, double hubFontSize) => size switch
    {
        GroupTitleSize.Large => Math.Round(hubFontSize + 3),
        GroupTitleSize.ExtraLarge => Math.Round(hubFontSize + 7),
        _ => style == GroupHeaderStyle.SmallCaps ? SmallCapsSize : hubFontSize,
    };

    /// <summary>OpenType weight: 700 bold, 600 semibold, 400 normal.</summary>
    public static int TitleWeight(ToolGroup group, GroupHeaderStyle style)
    {
        if (group.Bold) return 700;
        if (group.TitleFont == GroupTitleFont.Semibold) return 600;
        return style is GroupHeaderStyle.Bold or GroupHeaderStyle.SmallCaps ? 700 : 400;
    }

    /// <summary>Font family list for the title; empty = the hub's own font.</summary>
    public static string FontFamily(GroupTitleFont font) => font switch
    {
        GroupTitleFont.Serif => "Georgia, Cambria, Times New Roman",
        GroupTitleFont.Mono => "Cascadia Mono, Consolas",
        _ => string.Empty,
    };

    /// <summary>Logo size next to the title: a bit larger than the text, at least 14 px.</summary>
    public static double LogoSize(double titleSize) => Math.Max(14, Math.Round(titleSize + 5));

    /// <summary>
    /// A stand-in entry the logo cache can resolve, or null when the group has no logo. Its kind is a link, so an
    /// unreadable file never falls back to some other icon.
    /// </summary>
    public static ToolEntry? LogoCarrier(ToolGroup group)
    {
        var path = group.IconPath?.Trim() ?? string.Empty;
        if (path.Length == 0) return null;
        return new ToolEntry { Id = "group:" + group.Id, Name = group.Title, Kind = ToolKind.Url, IconPath = path };
    }
}
