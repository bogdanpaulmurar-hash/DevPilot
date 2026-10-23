namespace DevPilot.Models;

/// <summary>
/// Any item DevPilot manages: an application, a script, a folder, a URL, a document, a command.
/// Replaces the three v1 lists (apps, custom tools, hard-coded tool paths).
/// </summary>
public class ToolEntry
{
    public string Id { get; set; } = NewId.Short();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ToolKind Kind { get; set; } = ToolKind.Executable;

    /// <summary>Executable, script, folder, URL, document or command, depending on <see cref="Kind"/>.</summary>
    public string Path { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Python only: interpreter to use (empty = pythonw.exe from PATH). Point it at a venv's pythonw.exe if needed.</summary>
    public string Interpreter { get; set; } = string.Empty;
    /// <summary>Python only: run as <c>python -m module</c> instead of a script file.</summary>
    public string PythonModule { get; set; } = string.Empty;

    public bool RunAsAdmin { get; set; }
    /// <summary>Console tools: start without a visible console window.</summary>
    public bool HideWindow { get; set; }

    // ── Organisation ────────────────────────────────────────────────────
    public string GroupId { get; set; } = string.Empty;
    public int Order { get; set; }
    /// <summary>Hidden tools stay configured but are not shown in the hub.</summary>
    public bool Hidden { get; set; }

    // ── Look ────────────────────────────────────────────────────────────
    public string Glyph { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    /// <summary>Optional row colour (#RRGGBB): marker bar and/or row tint, see <see cref="ColorStyle"/>. For separators: the line colour.</summary>
    public string Color { get; set; } = string.Empty;
    public RowColorStyle ColorStyle { get; set; } = RowColorStyle.Marker;
    /// <summary>Optional colour of the name (#RRGGBB); empty = theme text colour.</summary>
    public string TextColor { get; set; } = string.Empty;
    /// <summary>Optional colour of the symbol (#RRGGBB); empty = theme colour.</summary>
    public string GlyphColor { get; set; } = string.Empty;
    /// <summary>Only used when <see cref="Kind"/> is <see cref="ToolKind.Separator"/>; the caption is <see cref="Name"/>.</summary>
    public SeparatorStyle SeparatorStyle { get; set; } = SeparatorStyle.Line;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    /// <summary>Shown as the large spotlight card at the top of the hub.</summary>
    public bool Spotlight { get; set; }

    /// <summary>Known tool this entry was created from (screenpilot, focuspilot, nexusx, regtester, autopilot) — empty for custom tools.</summary>
    public string Preset { get; set; } = string.Empty;

    public List<ToolAction> Actions { get; set; } = [];

    public TrayMode Tray { get; set; } = TrayMode.HubOnly;
    public ManagedOptions Managed { get; set; } = new();
    public FolderOptions Folder { get; set; } = new();
    public AutoStartOptions AutoStart { get; set; } = new();
    public MonitorOptions Monitor { get; set; } = new();

    public bool IsSeparator => Kind == ToolKind.Separator;
    public bool IsFolderLike => Kind is ToolKind.Folder or ToolKind.NetworkShare;
    public bool IsProcessLike => Kind is ToolKind.Executable or ToolKind.Python or ToolKind.PowerShell
        or ToolKind.Batch or ToolKind.VBScript or ToolKind.ShellCommand or ToolKind.Ssh or ToolKind.Wsl;
}

/// <summary>How DevPilot tells a launched tool that DevPilot owns tray, keep-awake and autostart.</summary>
public class ManagedOptions
{
    /// <summary>Pass DEVPILOT_MANAGED=1 and DEVPILOT_SUPPRESS=… to the tool.</summary>
    public bool Enabled { get; set; } = true;
    public bool SuppressKeepAwake { get; set; } = true;
    public bool SuppressAutostart { get; set; } = true;
    /// <summary>Extra arguments added only when the tool is managed and its tray is suppressed (e.g. --no-tray).</summary>
    public string NoTrayArguments { get; set; } = string.Empty;
    /// <summary>Extra environment variables (KEY=VALUE per line).</summary>
    public string ExtraEnvironment { get; set; } = string.Empty;
}

/// <summary>Folder entries: optionally browse the contents from the hub.</summary>
public class FolderOptions
{
    public bool ExpandInHub { get; set; }
    public int Depth { get; set; } = 2;
    public bool ShowFiles { get; set; } = true;
    public bool ShowFolders { get; set; } = true;
    public bool ShowHidden { get; set; }
    public int MaxItems { get; set; } = 40;
    /// <summary>Semicolon separated patterns, e.g. "*.xlsx;*.docx". Empty = all files.</summary>
    public string Filter { get; set; } = string.Empty;
    public bool FoldersFirst { get; set; } = true;
}

public class AutoStartOptions
{
    /// <summary>DevPilot starts this tool when DevPilot starts (replaces the tool's own Windows autostart).</summary>
    public bool Enabled { get; set; }
    public int DelaySeconds { get; set; } = 5;
    public int Order { get; set; }
    /// <summary>Arguments used for the automatic start instead of the normal ones (e.g. "gui --autostart"). Empty = normal arguments.</summary>
    public string Arguments { get; set; } = string.Empty;
}

public class MonitorOptions
{
    /// <summary>
    /// How to recognise the running tool. Empty = derived from the target
    /// (exe name, or script / module name inside a python command line).
    /// Several patterns can be separated with ';'.
    /// </summary>
    public string ProcessMatch { get; set; } = string.Empty;
    /// <summary>Optional health URL (e.g. http://127.0.0.1:8765/).</summary>
    public string HealthUrl { get; set; } = string.Empty;
    public bool AutoRestart { get; set; }
    public int MaxRestartsPerHour { get; set; } = 3;
}
