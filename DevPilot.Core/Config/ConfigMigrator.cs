using DevPilot.Core.Presets;
using DevPilot.Models;

namespace DevPilot.Core.Config;

/// <summary>Converts a v1 configuration into the v2 model without losing entries.</summary>
public static class ConfigMigrator
{
    public static AppSettings FromV1(LegacyV1Settings v1)
    {
        var s = Defaults.CreateEmpty();
        s.Theme = string.Equals(v1.Theme, "light", StringComparison.OrdinalIgnoreCase) ? ThemeMode.Light : ThemeMode.Dark;
        s.Window.CloseToTray = v1.MinimizeToTray;

        s.KeepAwake.Enabled = v1.JigglerEnabled;
        s.KeepAwake.IntervalSeconds = Math.Clamp(v1.JigglerIntervalSec, 10, 600);
        s.KeepAwake.Mode = v1.JigglerMethod.Contains("Key", StringComparison.OrdinalIgnoreCase)
            ? KeepAwakeMode.F15Key
            : KeepAwakeMode.MouseNudge;

        // v1 grouped by type; v2 uses user groups. Category → group, else a single "Tools" group.
        var groups = new Dictionary<string, ToolGroup>(StringComparer.OrdinalIgnoreCase);
        ToolGroup GroupFor(string title)
        {
            title = string.IsNullOrWhiteSpace(title) ? "Tools" : title.Trim();
            if (!groups.TryGetValue(title, out var g))
            {
                g = new ToolGroup { Title = title, Order = groups.Count };
                groups[title] = g;
            }
            return g;
        }

        int order = 0;
        foreach (var app in v1.Apps.OrderBy(a => a.SortOrder))
        {
            var kind = ParseKind(app.Type);
            var tool = new ToolEntry
            {
                Id = string.IsNullOrWhiteSpace(app.Id) ? NewId.Short() : app.Id,
                Name = string.IsNullOrWhiteSpace(app.Name) ? System.IO.Path.GetFileName(app.Path) : app.Name,
                Description = app.Description,
                Kind = kind,
                Path = app.Path,
                Arguments = app.Arguments,
                RunAsAdmin = app.RunAsAdmin,
                IconPath = app.IconPath,
                Color = NormalizeColor(app.TagColor),
                Bold = app.TrayFontStyle.Contains("Bold", StringComparison.OrdinalIgnoreCase),
                Italic = app.TrayFontStyle.Contains("Italic", StringComparison.OrdinalIgnoreCase),
                Hidden = !app.IsVisible,
                GroupId = GroupFor(app.Category).Id,
                Order = order++,
                HideWindow = kind == ToolKind.Python,
            };
            ToolPresets.ApplyIfRecognised(tool, overwriteUserFields: false);
            s.Tools.Add(tool);
        }

        void AddLegacyExe(string name, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (s.Tools.Any(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase))) return;
            s.Tools.Add(new ToolEntry
            {
                Name = name,
                Kind = ToolKind.Executable,
                Path = path,
                GroupId = GroupFor("Work tools").Id,
                Order = order++,
            });
        }

        // The three hard-coded v1 tool paths become normal tools (names kept generic).
        AddLegacyExe(LegacyName(v1.DataCleansingExePath, "Data Cleansing"), v1.DataCleansingExePath);
        AddLegacyExe(LegacyName(v1.TestClientExePath, "Test Client"), v1.TestClientExePath);
        AddLegacyExe(LegacyName(v1.TcMgmtStarterExePath, "TC Management Starter"), v1.TcMgmtStarterExePath);
        foreach (var ct in v1.CustomTools)
            AddLegacyExe(string.IsNullOrWhiteSpace(ct.Name) ? System.IO.Path.GetFileNameWithoutExtension(ct.ExePath) : ct.Name, ct.ExePath);

        foreach (var hb in v1.HeartbeatTargets)
        {
            s.Monitor.ExtraTargets.Add(new HealthTarget
            {
                Name = hb.Name,
                Target = hb.Target,
                IsUrl = string.Equals(hb.Type, "Url", StringComparison.OrdinalIgnoreCase),
                Enabled = hb.IsEnabled,
            });
        }

        int hk = 1;
        foreach (var h in v1.Hotkeys)
        {
            var action = h.Action switch
            {
                "ShowHide" => "main",
                "ToggleJiggler" => "keepawake",
                _ => string.Empty,
            };
            if (action.Length == 0 || string.IsNullOrWhiteSpace(h.Key)) continue;
            s.Hotkeys.Add(new HotkeyConfig { Id = hk++, Action = action, Modifiers = h.Modifiers, Key = h.Key, IsEnabled = h.IsEnabled });
        }
        if (s.Hotkeys.Count == 0) s.Hotkeys.AddRange(Defaults.DefaultHotkeys());

        s.Groups.AddRange(groups.Values);
        return s;
    }

    private static string LegacyName(string path, string fallback)
    {
        var n = System.IO.Path.GetFileNameWithoutExtension(path ?? string.Empty);
        return string.IsNullOrWhiteSpace(n) ? fallback : n;
    }

    public static ToolKind ParseKind(string? type) =>
        Enum.TryParse<ToolKind>(type, ignoreCase: true, out var k) ? k : ToolKind.Executable;

    public static string NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return string.Empty;
        color = color.Trim();
        if (!color.StartsWith('#')) color = "#" + color;
        if (color.Length is not (7 or 9)) return string.Empty;
        for (int i = 1; i < color.Length; i++)
            if (!Uri.IsHexDigit(color[i])) return string.Empty;
        return color.ToUpperInvariant();
    }
}
