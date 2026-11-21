using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevPilot.Core.Integrations;

/// <summary>A layout ZonePilot knows (built-in or one of the user's custom layouts).</summary>
public sealed record ZonePilotLayout(string Id, string Name, bool BuiltIn);

/// <summary>A monitor as <c>ZonePilot layouts</c> reports it.</summary>
public sealed record ZonePilotMonitor(int Index, string Key, string Bounds, double Scale, bool Primary, string LayoutId)
{
    public string Describe() =>
        $"Monitor {Index}{(Primary ? " (primary)" : string.Empty)}{(Bounds.Length > 0 ? " · " + Bounds : string.Empty)}{(Scale > 0 ? $" @{Scale * 100:0}%" : string.Empty)}";
}

/// <summary>Monitor changes: <c>apply</c> arguments to run after the reload, and monitor keys to clear in the file before it.</summary>
public sealed record ZonePilotMonitorPlan(IReadOnlyList<string> ApplyArguments, IReadOnlyList<string> ClearKeys)
{
    public bool IsEmpty => ApplyArguments.Count == 0 && ClearKeys.Count == 0;
}

/// <summary>What <c>ZonePilot layouts</c> printed: layouts, the default one and the monitors.</summary>
public sealed record ZonePilotLayoutList(string DefaultLayoutId, IReadOnlyList<ZonePilotLayout> Layouts, IReadOnlyList<ZonePilotMonitor> Monitors);

/// <summary>
/// Reads and edits ZonePilot's <c>%APPDATA%\ZonePilot\settings.json</c> (see ZonePilot's docs/DEVPILOT.md).
/// Only the fields DevPilot's Configure page shows are changed; every other value in the file is kept as it is.
/// </summary>
public sealed class ZonePilotSettings
{
    public static readonly string[] DragModifiers = ["Shift", "Ctrl", "Alt", "None"];

    /// <summary>The built-in layouts, in ZonePilot's order (ids from its LayoutTemplates).</summary>
    public static readonly IReadOnlyList<ZonePilotLayout> BuiltInLayouts =
    [
        new("columns-2", "Left / right", true),
        new("rows-2", "Top / bottom", true),
        new("grid-2x2", "Grid 2 x 2", true),
        new("grid-3x2", "Grid 3 x 2", true),
        new("columns-3", "Three columns", true),
        new("focus", "Focus", true),
    ];

    /// <summary>Hotkey fields of the <c>hotkeys</c> object (camelCase JSON name, label), excluding <c>snapToZoneModifiers</c>.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> HotkeyFields =
    [
        ("editor", "Open the editor"),
        ("moveLeft", "Move window to the zone on the left"),
        ("moveRight", "Move window to the zone on the right"),
        ("moveUp", "Move window to the zone above"),
        ("moveDown", "Move window to the zone below"),
    ];

    private static readonly Dictionary<string, string> HotkeyDefaults = new(StringComparer.Ordinal)
    {
        ["editor"] = "Win+Shift+Z",
        ["moveLeft"] = "Ctrl+Alt+Win+Left",
        ["moveRight"] = "Ctrl+Alt+Win+Right",
        ["moveUp"] = "Ctrl+Alt+Win+Up",
        ["moveDown"] = "Ctrl+Alt+Win+Down",
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly JsonObject _root;

    private ZonePilotSettings(JsonObject root) => _root = root;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZonePilot", "settings.json");

    /// <summary>The settings with ZonePilot's defaults (used when the file does not exist yet).</summary>
    public static ZonePilotSettings Empty() => new(new JsonObject());

    public static ZonePilotSettings Parse(string json)
    {
        var node = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json, documentOptions: ReadOptions);
        return new ZonePilotSettings(node as JsonObject ?? throw new JsonException("settings.json does not hold a JSON object."));
    }

    /// <summary>Reads the file; a missing file gives the defaults. Throws on unreadable JSON (the file is then left alone).</summary>
    public static ZonePilotSettings Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty();

    /// <summary>Writes atomically (temporary file, then replace), like ZonePilot itself.</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".devpilot.tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, path, overwrite: true);
    }

    public string ToJson() => _root.ToJsonString(WriteOptions);

    // ── Fields ───────────────────────────────────────────────────────────

    public string DefaultLayoutId
    {
        get => GetString(_root, "defaultLayoutId") is { Length: > 0 } id ? id : BuiltInLayouts[0].Id;
        set => _root[Name(_root, "defaultLayoutId")] = (value ?? string.Empty).Trim();
    }

    public bool DragEnabled
    {
        get => GetBool(Child("drag", create: false), "enabled", true);
        set => Child("drag")![Name(Child("drag")!, "enabled")] = value;
    }

    /// <summary><c>Shift</c>, <c>Ctrl</c>, <c>Alt</c> or <c>None</c>.</summary>
    public string DragModifier
    {
        get => NormalizeModifier(GetString(Child("drag", create: false), "modifier"));
        set => Child("drag")![Name(Child("drag")!, "modifier")] = NormalizeModifier(value);
    }

    public bool HotkeysEnabled
    {
        get => GetBool(Child("hotkeys", create: false), "enabled", true);
        set => Child("hotkeys")![Name(Child("hotkeys")!, "enabled")] = value;
    }

    /// <summary>One of <see cref="HotkeyFields"/> (e.g. <c>editor</c>); empty text means "not set".</summary>
    public string GetHotkey(string key) =>
        GetString(Child("hotkeys", create: false), key) ?? (HotkeyDefaults.TryGetValue(key, out var d) ? d : string.Empty);

    public void SetHotkey(string key, string? value)
    {
        var hotkeys = Child("hotkeys")!;
        hotkeys[Name(hotkeys, key)] = (value ?? string.Empty).Trim();
    }

    /// <summary>Modifiers combined with 1..9 to send the active window to that zone; empty disables it.</summary>
    public string SnapToZoneModifiers
    {
        get => GetString(Child("hotkeys", create: false), "snapToZoneModifiers") ?? "Ctrl+Alt+Win";
        set { var h = Child("hotkeys")!; h[Name(h, "snapToZoneModifiers")] = (value ?? string.Empty).Trim(); }
    }

    /// <summary>Monitor key → layout id, as stored in the file.</summary>
    public IReadOnlyDictionary<string, string> MonitorLayouts
    {
        get
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Get(_root, "monitorLayouts") is JsonObject map)
                foreach (var (k, v) in map)
                    if (v is JsonValue jv && jv.TryGetValue<string>(out var id)) result[k] = id;
            return result;
        }
    }

    /// <summary>Built-in layouts followed by the custom layouts saved in the file.</summary>
    public IReadOnlyList<ZonePilotLayout> Layouts()
    {
        var list = new List<ZonePilotLayout>(BuiltInLayouts);
        if (Get(_root, "customLayouts") is JsonArray custom)
        {
            foreach (var item in custom.OfType<JsonObject>())
            {
                var id = GetString(item, "id");
                if (string.IsNullOrWhiteSpace(id) || list.Any(l => l.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
                var name = GetString(item, "name");
                list.Add(new ZonePilotLayout(id, string.IsNullOrWhiteSpace(name) ? id : name, false));
            }
        }
        return list;
    }

    // ── Validation ───────────────────────────────────────────────────────

    private static readonly HashSet<string> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Left", "Up", "Right", "Down", "PageUp", "PageDown", "End", "Home", "Insert", "Delete", "Space", "Tab", "Enter", "Esc",
        "Backtick", "Escape", "Return", "PgUp", "PgDn", "Del", "Ins", "`", "Tilde",
    };

    /// <summary>
    /// Same rules as ZonePilot's HotkeyGesture: modifiers (Ctrl, Alt, Shift, Win) plus exactly one key
    /// (A–Z, 0–9, F1–F24, NumPad0–9, arrows, navigation keys); a key without modifiers is only allowed for F-keys.
    /// Empty text is valid and switches that hotkey off.
    /// </summary>
    public static bool IsValidHotkey(string? text, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return true;
        bool hasModifier = false;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsModifier(raw)) { hasModifier = true; continue; }
            if (key is not null) { error = "Only one key, e.g. Win+Shift+Z."; return false; }
            if (!IsKey(raw)) { error = $"'{raw}' is not a key ZonePilot knows."; return false; }
            key = raw;
        }
        if (key is null) { error = "Add a key after the modifiers, e.g. Win+Shift+Z."; return false; }
        if (!hasModifier && !IsFunctionKey(key)) { error = "Add Ctrl, Alt, Shift or Win (only F-keys may stand alone)."; return false; }
        return true;
    }

    /// <summary>A modifier-only list such as <c>Ctrl+Alt+Win</c>; empty is valid (switches it off).</summary>
    public static bool IsValidModifiers(string? text, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(p => !IsModifier(p)))
        {
            error = "Only Ctrl, Alt, Shift and Win, e.g. Ctrl+Alt+Win.";
            return false;
        }
        return true;
    }

    public static string NormalizeModifier(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "ctrl" or "control" => "Ctrl",
        "alt" => "Alt",
        "none" => "None",
        _ => "Shift",
    };

    private static bool IsModifier(string s) =>
        s.ToLowerInvariant() is "ctrl" or "control" or "alt" or "shift" or "win" or "windows" or "meta" or "super";

    private static bool IsFunctionKey(string s) =>
        s.Length is 2 or 3 && (s[0] is 'F' or 'f') && int.TryParse(s[1..], out var n) && n is >= 1 and <= 24;

    private static bool IsKey(string s)
    {
        if (s.Length == 1 && char.IsAsciiLetterOrDigit(s[0])) return true;
        if (s.Length == 2 && s[0] is 'D' or 'd' && char.IsAsciiDigit(s[1])) return true;
        if (IsFunctionKey(s)) return true;
        if (s.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && s.Length == 7 && char.IsAsciiDigit(s[6])) return true;
        return NamedKeys.Contains(s);
    }

    // ── JSON helpers (ZonePilot reads keys case-insensitively, so an existing spelling is kept) ──

    private JsonObject? Child(string name, bool create = true)
    {
        if (Get(_root, name) is JsonObject existing) return existing;
        if (!create) return null;
        var o = new JsonObject();
        _root[Name(_root, name)] = o;
        return o;
    }

    private static string Name(JsonObject o, string name) =>
        o.Select(p => p.Key).FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;

    private static JsonNode? Get(JsonObject? o, string name) =>
        o is null ? null : o.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? GetString(JsonObject? o, string name) =>
        Get(o, name) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool GetBool(JsonObject? o, string name, bool fallback) =>
        Get(o, name) is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    // ── CLI ──────────────────────────────────────────────────────────────

    /// <summary>Arguments for <c>ZonePilot apply &lt;layout&gt; --monitor &lt;n&gt;</c>.</summary>
    public static string ApplyArguments(string layoutId, int monitorIndex) =>
        $"apply {Launch.CommandLineText.Quote(layoutId.Trim())} --monitor {monitorIndex}";

    public const string ReloadArguments = "reload";
    public const string LayoutsArguments = "layouts";

    /// <summary>ZonePilot's exit codes (0 ok, 1 error, 2 usage, 3 not running, 4 not found).</summary>
    public const int ExitNotRunning = 3;

    /// <summary>Parses the JSON printed by <c>ZonePilot layouts</c>; null when it is not that JSON.</summary>
    public static ZonePilotLayoutList? ParseLayouts(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var start = output.IndexOf('{');
        if (start < 0) return null;
        JsonObject? root;
        try { root = JsonNode.Parse(output[start..], documentOptions: ReadOptions) as JsonObject; }
        catch (JsonException) { return null; }
        if (root is null) return null;

        var layouts = new List<ZonePilotLayout>();
        if (Get(root, "layouts") is JsonArray arr)
        {
            foreach (var l in arr.OfType<JsonObject>())
            {
                var id = GetString(l, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                var name = GetString(l, "name");
                layouts.Add(new ZonePilotLayout(id, string.IsNullOrWhiteSpace(name) ? id : name, GetBool(l, "builtIn", false)));
            }
        }
        var monitors = new List<ZonePilotMonitor>();
        if (Get(root, "monitors") is JsonArray mons)
        {
            foreach (var m in mons.OfType<JsonObject>())
            {
                int index = Get(m, "index") is JsonValue iv && iv.TryGetValue<int>(out var i) ? i : 0;
                if (index <= 0) continue;
                double scale = Get(m, "scale") is JsonValue sv && sv.TryGetValue<double>(out var d) ? d : 0;
                monitors.Add(new ZonePilotMonitor(index, GetString(m, "key") ?? string.Empty, GetString(m, "bounds") ?? string.Empty,
                    scale, GetBool(m, "primary", false), GetString(m, "layout") ?? string.Empty));
            }
        }
        return new ZonePilotLayoutList(GetString(root, "defaultLayout") ?? string.Empty, layouts, monitors.OrderBy(m => m.Index).ToList());
    }

    /// <summary>
    /// What to do so each monitor ends up with the chosen layout. <paramref name="chosen"/> maps a monitor index to a
    /// layout id, or to an empty string for "follow the default layout". Monitors whose choice equals what the file
    /// already stores are left alone. Explicit layouts are set with <c>apply &lt;layout&gt; --monitor &lt;n&gt;</c>, run
    /// after <c>reload</c> so the running app does not overwrite the edited file with its older copy; a monitor going
    /// back to the default loses its entry in <c>monitorLayouts</c> (edited in the file before the reload).
    /// </summary>
    public ZonePilotMonitorPlan PlanMonitorChanges(IEnumerable<ZonePilotMonitor> monitors, IReadOnlyDictionary<int, string> chosen)
    {
        var stored = MonitorLayouts;
        var apply = new List<string>();
        var clear = new List<string>();
        foreach (var m in monitors.OrderBy(m => m.Index))
        {
            if (!chosen.TryGetValue(m.Index, out var id)) continue;
            id = (id ?? string.Empty).Trim();
            stored.TryGetValue(m.Key, out var current);
            current ??= string.Empty;
            if (string.Equals(current, id, StringComparison.OrdinalIgnoreCase)) continue;
            if (id.Length == 0) { if (m.Key.Length > 0) clear.Add(m.Key); }
            else apply.Add(ApplyArguments(id, m.Index));
        }
        return new ZonePilotMonitorPlan(apply, clear);
    }

    /// <summary>The layout stored for a monitor key, or empty when it follows the default layout.</summary>
    public string MonitorLayoutFor(string key) =>
        key.Length > 0 && MonitorLayouts.TryGetValue(key, out var id) ? id : string.Empty;

    /// <summary>Removes a monitor's own layout so it follows the default layout again.</summary>
    public bool ClearMonitorLayout(string key)
    {
        if (Get(_root, "monitorLayouts") is not JsonObject map) return false;
        var name = map.Select(p => p.Key).FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        return name is not null && map.Remove(name);
    }
}
