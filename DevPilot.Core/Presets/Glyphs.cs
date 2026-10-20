using DevPilot.Models;

namespace DevPilot.Core.Presets;

public sealed record GlyphInfo(string Glyph, string Name);

/// <summary>
/// Curated monochrome symbols (Segoe Fluent Icons / Segoe MDL2 Assets code points).
/// Any other character (letters, symbols, emoji) can be typed instead.
/// </summary>
public static class Glyphs
{
    public const string App = "";
    public const string Code = "";
    public const string Terminal = "";
    public const string Folder = "";
    public const string FolderOpen = "";
    public const string Document = "";
    public const string Globe = "";
    public const string Link = "";
    public const string Camera = "";
    public const string Video = "";
    public const string Record = "";
    public const string Picture = "";
    public const string Eye = "";
    public const string Bolt = "";
    public const string Diagnostic = "";
    public const string Settings = "";
    public const string Search = "";
    public const string Play = "";
    public const string Stop = "";
    public const string Refresh = "";
    public const string Close = "";
    public const string Add = "";
    public const string Delete = "";
    public const string Pin = "";
    public const string Unpin = "";
    public const string Power = "";
    public const string ChevronRight = "";
    public const string ChevronLeft = "";
    public const string ChevronDown = "";
    public const string ChevronUp = "";
    public const string Home = "";
    public const string History = "";
    public const string Clock = "";
    public const string Info = "";
    public const string Warning = "";
    public const string Check = "";
    public const string Star = "";
    public const string Color = "";
    public const string Copy = "";
    public const string Moon = "";
    public const string Sun = "";
    public const string Monitor = "";
    public const string Lock = "";
    public const string Mail = "";
    public const string Calendar = "";
    public const string OpenFile = "";
    public const string Download = "";
    public const string List = "";
    public const string More = "";
    public const string Tag = "";
    public const string Flag = "";
    public const string Admin = "";
    public const string Sync = "";
    public const string Save = "";
    public const string Edit = "";
    public const string Library = "";
    public const string Repair = "";
    public const string Keyboard = "";
    public const string Mouse = "";
    public const string Grid = "";

    /// <summary>The picker list shown in the editor (glyph, name).</summary>
    public static readonly IReadOnlyList<GlyphInfo> Catalogue =
    [
        new(App, "App"), new(Code, "Code"), new(Terminal, "Terminal"), new(Folder, "Folder"), new(FolderOpen, "Folder open"),
        new(Document, "Document"), new(Globe, "Web"), new(Link, "Link"), new(Camera, "Camera"), new(Video, "Video"),
        new(Record, "Record"), new(Picture, "Picture"), new(Eye, "Eye"), new(Bolt, "Bolt"), new(Diagnostic, "Diagnostic"),
        new(Settings, "Settings"), new(Search, "Search"), new(Play, "Play"), new(Stop, "Stop"), new(Refresh, "Refresh"),
        new(Home, "Home"), new(History, "History"), new(Clock, "Clock"), new(Info, "Info"), new(Warning, "Warning"),
        new(Check, "Check"), new(Star, "Star"), new(Color, "Colour"), new(Copy, "Copy"), new(Moon, "Moon"), new(Sun, "Sun"),
        new(Monitor, "Monitor"), new(Lock, "Lock"), new(Mail, "Mail"), new(Calendar, "Calendar"), new(OpenFile, "Open file"),
        new(Download, "Download"), new(List, "List"), new(Tag, "Tag"), new(Flag, "Flag"), new(Admin, "Admin"), new(Sync, "Sync"),
        new(Save, "Save"), new(Edit, "Edit"), new(Library, "Library"), new(Repair, "Repair"), new(Keyboard, "Keyboard"),
        new(Mouse, "Mouse"), new(Power, "Power"), new(Pin, "Pin"), new(Grid, "Grid"),
    ];

    public static string ForKind(ToolKind kind) => kind switch
    {
        ToolKind.Executable => App,
        ToolKind.Python => Code,
        ToolKind.PowerShell => Terminal,
        ToolKind.Batch => Terminal,
        ToolKind.VBScript => Code,
        ToolKind.ShellCommand => Terminal,
        ToolKind.Folder => Folder,
        ToolKind.NetworkShare => Folder,
        ToolKind.Url => Globe,
        ToolKind.Document => Document,
        ToolKind.Ssh => Terminal,
        ToolKind.Wsl => Terminal,
        _ => App,
    };

    public static string ForAction(ActionKind kind) => kind switch
    {
        ActionKind.Url => Globe,
        ActionKind.Folder => Folder,
        ActionKind.File => Document,
        ActionKind.Command => Terminal,
        _ => Play,
    };

    /// <summary>Accepts a glyph, "E722", "U+E722" or "" and returns the character string.</summary>
    public static string Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var s = input.Trim();
        var hex = s;
        if (hex.StartsWith("\\u", StringComparison.OrdinalIgnoreCase) || hex.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];
        else if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];
        if (hex.Length is 4 or 5 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var cp)
            && s.Length >= 4)
            return char.ConvertFromUtf32(cp);
        return s;
    }
}
