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

public enum TrayClickAction { OpenHub, OpenMainWindow, QuickMenu, Nothing }

public enum ToolRunState { Unknown, Stopped, Running, Starting, Failed }
