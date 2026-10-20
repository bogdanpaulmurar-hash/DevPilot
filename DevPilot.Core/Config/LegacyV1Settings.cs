using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevPilot.Core.Config;

/// <summary>Read-only copy of the v1 config layout (used only for migration).</summary>
public sealed class LegacyV1Settings
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    [JsonPropertyName("theme")] public string Theme { get; set; } = "dark";
    [JsonPropertyName("minimize_to_tray")] public bool MinimizeToTray { get; set; } = true;
    [JsonPropertyName("jiggler_enabled")] public bool JigglerEnabled { get; set; }
    [JsonPropertyName("jiggler_interval_sec")] public int JigglerIntervalSec { get; set; } = 60;
    [JsonPropertyName("jiggler_method")] public string JigglerMethod { get; set; } = "Mouse Move";
    [JsonPropertyName("heartbeat_interval_sec")] public int HeartbeatIntervalSec { get; set; } = 30;
    [JsonPropertyName("heartbeat_targets")] public List<LegacyHeartbeatTarget> HeartbeatTargets { get; set; } = [];
    [JsonPropertyName("data_cleansing_exe_path")] public string DataCleansingExePath { get; set; } = string.Empty;
    [JsonPropertyName("test_client_exe_path")] public string TestClientExePath { get; set; } = string.Empty;
    [JsonPropertyName("tc_mgmt_starter_exe_path")] public string TcMgmtStarterExePath { get; set; } = string.Empty;
    [JsonPropertyName("apps")] public List<LegacyAppEntry> Apps { get; set; } = [];
    [JsonPropertyName("custom_tools")] public List<LegacyCustomTool> CustomTools { get; set; } = [];
    [JsonPropertyName("hotkeys")] public List<LegacyHotkey> Hotkeys { get; set; } = [];
}

public sealed class LegacyAppEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("args")] public string Arguments { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = "Executable";
    [JsonPropertyName("is_visible")] public bool IsVisible { get; set; } = true;
    [JsonPropertyName("run_as_admin")] public bool RunAsAdmin { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("icon_path")] public string IconPath { get; set; } = string.Empty;
    [JsonPropertyName("sort_order")] public int SortOrder { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
    [JsonPropertyName("tray_font_style")] public string TrayFontStyle { get; set; } = "Normal";
    [JsonPropertyName("show_in_tray")] public bool ShowInTray { get; set; }
    [JsonPropertyName("tag_color")] public string TagColor { get; set; } = string.Empty;
}

public sealed class LegacyCustomTool
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("exe_path")] public string ExePath { get; set; } = string.Empty;
}

public sealed class LegacyHeartbeatTarget
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("target")] public string Target { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = "Process";
    [JsonPropertyName("is_enabled")] public bool IsEnabled { get; set; } = true;
}

public sealed class LegacyHotkey
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Modifiers { get; set; } = "Ctrl+Shift";
    public string Key { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}
