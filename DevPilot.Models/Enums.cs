namespace DevPilot.Models;

/// <summary>What a tool entry points to and how it is launched.</summary>
public enum ToolKind
{
    Executable,
    Python,
    PowerShell,
    Batch,
    VBScript,
    ShellCommand,
    Folder,
    NetworkShare,
    Url,
    Document,
    Ssh,
    Wsl,
    /// <summary>Not a tool: a line, gap or label between rows in the hub.</summary>
    Separator,
}

/// <summary>How the colour of a hub row (tool, group or separator) is shown.</summary>
public enum RowColorStyle
{
    /// <summary>Thin bar on the left of the row.</summary>
    Marker,
    /// <summary>Soft tint over the whole row.</summary>
    Highlight,
    /// <summary>Both the bar and the tint.</summary>
    MarkerAndHighlight,
}

/// <summary>What a hub row shows next to (or instead of) its name: logo, symbol or nothing.</summary>
public enum HubLabelMode
{
    /// <summary>Per entry only: follow the hub-wide setting (Appearance).</summary>
    Default,
    /// <summary>Logo and name when the entry has a logo, otherwise symbol and name.</summary>
    Auto,
    /// <summary>Logo and name (symbol and name when there is no logo).</summary>
    LogoAndText,
    /// <summary>Only the logo; the name moves to the tooltip (symbol and name when there is no logo).</summary>
    LogoOnly,
    /// <summary>Symbol and name: the classic look.</summary>
    TextAndSymbol,
    /// <summary>Only the name, no symbol and no logo.</summary>
    TextOnly,
}

/// <summary>How a separator row looks.</summary>
public enum SeparatorStyle
{
    /// <summary>A thin horizontal line.</summary>
    Line,
    /// <summary>An empty gap.</summary>
    Space,
    /// <summary>A small caption followed by a line.</summary>
    Label,
}

/// <summary>How a tool is represented in the notification area.</summary>
public enum TrayMode
{
    /// <summary>Only inside the DevPilot hub. DevPilot asks the tool to hide its own icon.</summary>
    HubOnly,
    /// <summary>The tool keeps its own tray icon (DevPilot does not ask it to hide it).</summary>
    Native,
    /// <summary>DevPilot draws a separate tray icon for the tool, with the tool's actions.</summary>
    DevPilotIcon,
}

/// <summary>What a tool action does when clicked.</summary>
public enum ActionKind
{
    /// <summary>Runs the tool's own target (exe / script / module) with these arguments.</summary>
    Arguments,
    /// <summary>Runs an arbitrary command line (Value = program, Arguments = args).</summary>
    Command,
    /// <summary>Opens a URL in the default browser.</summary>
    Url,
    /// <summary>Opens a folder in Explorer.</summary>
    Folder,
    /// <summary>Opens a file with its default application.</summary>
    File,
    /// <summary>A visual separator between actions.</summary>
    Separator,
}

public enum KeepAwakeMode
{
    /// <summary>Power request only (no input is simulated). Recommended.</summary>
    Silent,
    /// <summary>Power request plus a zero-distance mouse event (resets the idle timer, cursor does not move).</summary>
    MouseNudge,
    /// <summary>Power request plus an F15 key tap (no visible effect in normal apps).</summary>
    F15Key,
}

public enum ThemeMode { Dark, Light, System }

public enum HubDensity { Compact, Comfortable, Spacious }

public enum HubAnchor { Auto, BottomRight, BottomLeft, TopRight, TopLeft, Center }

public enum FlyoutSide { Auto, Left, Right }

public enum GroupHeaderStyle { SmallCaps, Normal, Bold, Hidden }

public enum GroupTitleAlign { Left, Center }

/// <summary>Size of one group's title, relative to the hub font size.</summary>
public enum GroupTitleSize { Default, Large, ExtraLarge }

/// <summary>A few title fonts, enough to set a group apart from the tools under it.</summary>
public enum GroupTitleFont { Default, Semibold, Serif, Mono }

public enum TrayClickAction { OpenHub, OpenMainWindow, QuickMenu, Nothing }

public enum ToolRunState { Unknown, Stopped, Running, Starting, Failed }

/// <summary>When DevPilot makes a backup on its own.</summary>
public enum BackupMode
{
    /// <summary>Only when the user clicks "Back up now".</summary>
    Manual,
    /// <summary>At most one automatic backup per day, shortly after DevPilot starts.</summary>
    Daily,
    /// <summary>One automatic backup every time DevPilot starts.</summary>
    EveryStart,
}
