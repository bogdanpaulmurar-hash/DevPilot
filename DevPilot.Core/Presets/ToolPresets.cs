using DevPilot.Models;

namespace DevPilot.Core.Presets;

/// <summary>A known tool DevPilot can configure automatically.</summary>
public sealed record ToolPreset(
    string Key,
    string Name,
    string Description,
    ToolKind Kind,
    string Glyph,
    string PythonModule,
    string DefaultArguments,
    string AutoStartArguments,
    string NoTrayArguments,
    string ProcessMatch,
    string HealthUrl,
    bool Spotlight,
    string[] PathHints,
    string[] StartupNames,
    bool HasOwnKeepAwake,
    bool HasOwnTray,
    bool SupportsSuppressEnv,
    Func<List<ToolAction>> Actions,
    string Notes);

/// <summary>Catalogue of the user's own tools (actions mirror their native tray menus).</summary>
public static class ToolPresets
{
    private static ToolAction A(string label, string glyph, ActionKind kind, string value = "", string args = "",
        string section = "", bool featured = false)
        => new() { Label = label, Glyph = glyph, Kind = kind, Value = value, Arguments = args, Section = section, Featured = featured };

    public static readonly ToolPreset ScreenPilot = new(
        Key: "screenpilot",
        Name: "ScreenPilot",
        Description: "Screenshots, recordings and documentation",
        Kind: ToolKind.Python,
        Glyph: Glyphs.Camera,
        PythonModule: "screenpilot",
        DefaultArguments: "gui",
        AutoStartArguments: "gui --autostart",
        NoTrayArguments: "",
        ProcessMatch: "screenpilot",
        HealthUrl: "",
        Spotlight: true,
        PathHints: ["screenpilot"],
        StartupNames: ["ScreenPilot"],
        HasOwnKeepAwake: false,
        HasOwnTray: true,
        SupportsSuppressEnv: false,
        Actions: () =>
        [
            A("Window", Glyphs.App, ActionKind.Arguments, args: "capture window", section: "Capture", featured: true),
            A("Current monitor", Glyphs.Monitor, ActionKind.Arguments, args: "capture current", section: "Capture", featured: true),
            A("All monitors", Glyphs.Grid, ActionKind.Arguments, args: "capture all", section: "Capture", featured: true),
            A("Record 10 s", Glyphs.Record, ActionKind.Arguments, args: "record --seconds 10", section: "Capture", featured: true),
            A("Current monitor in 3 s", Glyphs.Clock, ActionKind.Arguments, args: "capture current --delay 3", section: "Delayed"),
            A("Current monitor in 5 s", Glyphs.Clock, ActionKind.Arguments, args: "capture current --delay 5", section: "Delayed"),
            A("Current monitor in 10 s", Glyphs.Clock, ActionKind.Arguments, args: "capture current --delay 10", section: "Delayed"),
            A("Record 30 s (MP4)", Glyphs.Video, ActionKind.Arguments, args: "record --seconds 30", section: "Recording"),
            A("Record 10 s (GIF)", Glyphs.Video, ActionKind.Arguments, args: "record --seconds 10 --format gif", section: "Recording"),
            A("Captures folder", Glyphs.FolderOpen, ActionKind.Folder, value: @"%USERPROFILE%\Pictures\ScreenPilot", section: "Files"),
            A("Export diagnostics", Glyphs.Diagnostic, ActionKind.Arguments,
                args: "diagnostics --out \"%USERPROFILE%\\Desktop\\screenpilot-diagnostics.zip\"", section: "Files"),
        ],
        Notes: "Capture actions use the ScreenPilot command line. Region selection, history and settings stay in the ScreenPilot window.");

    public static readonly ToolPreset FocusPilot = new(
        Key: "focuspilot",
        Name: "FocusPilot",
        Description: "Visual highlighting rules over any window",
        Kind: ToolKind.Python,
        Glyph: Glyphs.Eye,
        PythonModule: "",
        DefaultArguments: "",
        AutoStartArguments: "",
        NoTrayArguments: "",
        ProcessMatch: "FocusPilot.pyw",
        HealthUrl: "",
        Spotlight: false,
        PathHints: ["focuspilot"],
        StartupNames: ["FocusPilot"],
        HasOwnKeepAwake: true,
        HasOwnTray: true,
        SupportsSuppressEnv: false,
        Actions: () =>
        [
            A("Rules file (config.json)", Glyphs.Document, ActionKind.File, value: @"{ToolDir}\config.json"),
            A("Rules guide", Glyphs.Info, ActionKind.File, value: @"{ToolDir}\GHID_REGULI.md"),
        ],
        Notes: "Has its own keep-awake (on by default) and Windows autostart. DevPilot takes over autostart; keep-awake is turned off in FocusPilot once it reads DEVPILOT_SUPPRESS.");

    public static readonly ToolPreset NexusX = new(
        Key: "nexusx",
        Name: "NexusX",
        Description: "Code export and link checking",
        Kind: ToolKind.Python,
        Glyph: Glyphs.Link,
        PythonModule: "nexusx",
        DefaultArguments: "",
        AutoStartArguments: "--minimized",
        NoTrayArguments: "",
        ProcessMatch: "nexusx",
        HealthUrl: "",
        Spotlight: false,
        PathHints: ["nexusx", "ucex"],
        StartupNames: ["NexusX"],
        HasOwnKeepAwake: false,
        HasOwnTray: true,
        SupportsSuppressEnv: false,
        Actions: () =>
        [
            A("Start minimized", Glyphs.Moon, ActionKind.Arguments, args: "--minimized"),
        ],
        Notes: "Scan/export, link check and reports run inside the NexusX window.");

    public static readonly ToolPreset RegTester = new(
        Key: "regtester",
        Name: "regTester",
        Description: "Regression test planning (local web app)",
        Kind: ToolKind.Python,
        Glyph: Glyphs.Diagnostic,
        PythonModule: "",
        DefaultArguments: "",
        AutoStartArguments: "--tray",
        NoTrayArguments: "--no-tray",
        ProcessMatch: "regtester.py",
        HealthUrl: "http://127.0.0.1:8765/",
        Spotlight: false,
        PathHints: ["regtester"],
        StartupNames: ["regTester"],
        HasOwnKeepAwake: false,
        HasOwnTray: true,
        SupportsSuppressEnv: true,
        Actions: () =>
        [
            A("Open", Glyphs.Globe, ActionKind.Url, value: "http://127.0.0.1:8765/"),
            A("Timeline", Glyphs.Calendar, ActionKind.Url, value: "http://127.0.0.1:8765/#/timeline"),
            A("Specifications", Glyphs.List, ActionKind.Url, value: "http://127.0.0.1:8765/#/specs"),
            A("Settings", Glyphs.Settings, ActionKind.Url, value: "http://127.0.0.1:8765/#/settings"),
            A("Log", Glyphs.History, ActionKind.Url, value: "http://127.0.0.1:8765/#/logs"),
        ],
        Notes: "All tray options are web pages, so they work 1:1 from DevPilot. Supports --no-tray.");

    public static readonly ToolPreset AutoPilot = new(
        Key: "autopilot",
        Name: "AutoPilot DC",
        Description: "No-code UI automation",
        Kind: ToolKind.Executable,
        Glyph: Glyphs.Bolt,
        PythonModule: "",
        DefaultArguments: "",
        AutoStartArguments: "",
        NoTrayArguments: "",
        ProcessMatch: "",
        HealthUrl: "",
        Spotlight: false,
        PathHints: ["autopilot"],
        StartupNames: ["AutoPilot", "AutoPilotDC"],
        HasOwnKeepAwake: true,
        HasOwnTray: false,
        SupportsSuppressEnv: false,
        Actions: () => [],
        Notes: "Keeps the PC awake only while a flow runs; that does not conflict with DevPilot.");

    public static readonly IReadOnlyList<ToolPreset> All = [ScreenPilot, FocusPilot, NexusX, RegTester, AutoPilot];

    public static ToolPreset? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Recognises a known tool from its name, path or module.</summary>
    public static ToolPreset? Recognise(ToolEntry tool)
    {
        if (Find(tool.Preset) is { } p) return p;
        var hay = $"{tool.Name}|{tool.Path}|{tool.PythonModule}|{tool.Arguments}".ToLowerInvariant().Replace(" ", "");
        return All.FirstOrDefault(pr => pr.PathHints.Any(h => hay.Contains(h)));
    }

    /// <summary>
    /// Creates a new tool from a preset. <paramref name="path"/> is what the user picked:
    /// a script / exe file, or (for module based Python tools) the project folder.
    /// </summary>
    public static ToolEntry Create(ToolPreset preset, string path = "", string groupId = "")
    {
        var t = new ToolEntry { Name = preset.Name, GroupId = groupId };
        SetTarget(t, preset, path);
        Apply(t, preset, overwriteUserFields: true);
        return t;
    }

    /// <summary>Puts <paramref name="path"/> in the right field for the preset (Path or WorkingDirectory).</summary>
    public static void SetTarget(ToolEntry t, ToolPreset preset, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        bool looksLikeFile = System.IO.Path.HasExtension(path) && !path.EndsWith("\\") && !path.EndsWith("/");
        if (preset.Kind == ToolKind.Python && !string.IsNullOrEmpty(preset.PythonModule) && !looksLikeFile)
        {
            t.WorkingDirectory = path;
            t.Path = string.Empty;
        }
        else
        {
            t.Path = path;
        }
    }

    public static bool ApplyIfRecognised(ToolEntry tool, bool overwriteUserFields)
    {
        var p = Recognise(tool);
        if (p is null) return false;
        Apply(tool, p, overwriteUserFields);
        return true;
    }

    /// <summary>Fills preset defaults. With <paramref name="overwriteUserFields"/> false only empty fields are filled.</summary>
    public static void Apply(ToolEntry t, ToolPreset p, bool overwriteUserFields)
    {
        bool Fill(string current) => overwriteUserFields || string.IsNullOrWhiteSpace(current);
        bool isScript = t.Path.EndsWith(".py", StringComparison.OrdinalIgnoreCase)
                        || t.Path.EndsWith(".pyw", StringComparison.OrdinalIgnoreCase);

        t.Preset = p.Key;
        if (Fill(t.Description)) t.Description = p.Description;
        if (Fill(t.Glyph)) t.Glyph = p.Glyph;
        if (overwriteUserFields)
        {
            t.Kind = p.Kind;
            t.Spotlight = p.Spotlight;
            t.Tray = p.HasOwnTray ? TrayMode.HubOnly : TrayMode.Native;
        }
        else if (p.Kind == ToolKind.Python && isScript)
        {
            t.Kind = ToolKind.Python;
        }

        if (t.Kind == ToolKind.Python)
        {
            t.HideWindow = true;
            bool moduleMode = !string.IsNullOrEmpty(p.PythonModule) && string.IsNullOrWhiteSpace(t.Path);
            if (moduleMode && Fill(t.PythonModule)) t.PythonModule = p.PythonModule;
        }
        if (Fill(t.Arguments)) t.Arguments = p.DefaultArguments;
        if (Fill(t.AutoStart.Arguments)) t.AutoStart.Arguments = p.AutoStartArguments;
        if (Fill(t.Managed.NoTrayArguments)) t.Managed.NoTrayArguments = p.NoTrayArguments;
        if (Fill(t.Monitor.ProcessMatch)) t.Monitor.ProcessMatch = p.ProcessMatch;
        if (Fill(t.Monitor.HealthUrl)) t.Monitor.HealthUrl = p.HealthUrl;
        if (overwriteUserFields || t.Actions.Count == 0) t.Actions = p.Actions();
    }
}
