namespace DevPilot.Models;

/// <summary>User defined group shown as a titled section in the hub.</summary>
public class ToolGroup
{
    public string Id { get; set; } = NewId.Short();
    public string Title { get; set; } = "Group";
    public string Glyph { get; set; } = string.Empty;
    /// <summary>Optional colour (#RRGGBB): dot and/or header tint, see <see cref="ColorStyle"/>.</summary>
    public string Color { get; set; } = string.Empty;
    public RowColorStyle ColorStyle { get; set; } = RowColorStyle.Marker;
    /// <summary>Optional title colour (#RRGGBB); empty = theme colour.</summary>
    public string TextColor { get; set; } = string.Empty;
    /// <summary>Optional symbol colour (#RRGGBB); empty = theme colour.</summary>
    public string GlyphColor { get; set; } = string.Empty;
    /// <summary>Optional logo (.png, .ico, .jpg, .bmp, or an .exe / .dll / .lnk to take the icon from) shown before the title.</summary>
    public string IconPath { get; set; } = string.Empty;
    /// <summary>Title placement in the header row; the count and the chevron stay on the right.</summary>
    public GroupTitleAlign TitleAlign { get; set; } = GroupTitleAlign.Left;
    /// <summary>Title size; <see cref="GroupTitleSize.Default"/> follows the hub's group header style.</summary>
    public GroupTitleSize TitleSize { get; set; } = GroupTitleSize.Default;
    public GroupTitleFont TitleFont { get; set; } = GroupTitleFont.Default;
    /// <summary>Bold title even when the hub's header style is not bold.</summary>
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public int Order { get; set; }
    public bool Collapsed { get; set; }
    public bool Hidden { get; set; }
}
