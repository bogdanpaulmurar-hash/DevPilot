// ============================================================
// DevPilot — IT Automation Hub v1.0
// AppSettings.cs — Root config model (persisted as config.json)
// Phase: 13 (+HideJigglerInTray, +TrayTypeConfigs)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DevPilot.Models;

public class AppSettings
{
    // ── Appearance ────────────────────────────────────────────────────────
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "dark";

    // ── Window behaviour ──────────────────────────────────────────────────
    [JsonPropertyName("minimize_to_tray")]
    public bool MinimizeToTray { get; set; } = true;

    // ── Tab Visibility ────────────────────────────────────────────────────
    [JsonPropertyName("tab_visibility")]
    public Dictionary<string, bool> TabVisibility { get; set; } = new()
    {
        ["Tools"]      = true,
        ["Jiggler"]    = true,
        ["Screenshot"] = true,
        ["Heartbeat"]  = true,
    };

    // ── Mouse Jiggler ─────────────────────────────────────────────────────
    [JsonPropertyName("jiggler_enabled")]
    public bool JigglerEnabled { get; set; } = false;

    [JsonPropertyName("jiggler_interval_sec")]
    public int JigglerIntervalSec { get; set; } = 60;

    /// <summary>"Mouse Move" or "Key Press"</summary>
    [JsonPropertyName("jiggler_method")]
    public string JigglerMethod { get; set; } = "Mouse Move";

    // ── Heartbeat ─────────────────────────────────────────────────────────
    [JsonPropertyName("heartbeat_enabled")]
    public bool HeartbeatEnabled { get; set; } = false;

    [JsonPropertyName("heartbeat_interval_sec")]
    public int HeartbeatIntervalSec { get; set; } = 30;

    [JsonPropertyName("heartbeat_targets")]
    public List<HeartbeatTarget> HeartbeatTargets { get; set; } = [];

    [JsonPropertyName("heartbeat_auto_restart")]
    public bool HeartbeatAutoRestart { get; set; } = false;

    // ── Screenshot ────────────────────────────────────────────────────────
    [JsonPropertyName("screenshot_save_folder")]
    public string ScreenshotSaveFolder { get; set; } = string.Empty;

    // ── Tool Paths ────────────────────────────────────────────────────────
    [JsonPropertyName("data_cleansing_exe_path")]
    public string DataCleansingExePath { get; set; } =
        @"D:\DataCleansingWork\Sources\DCWrapperStarterApp\DataCleansing.MasterApp.Starter.exe";

    [JsonPropertyName("test_client_exe_path")]
    public string TestClientExePath { get; set; } =
        @"D:\TestBenchAutomationWork\Sources\TestClientUtilityApp\TestClientUtilityApp.exe";

    [JsonPropertyName("tc_mgmt_starter_exe_path")]
    public string TcMgmtStarterExePath { get; set; } =
        @"D:\TestBenchAutomationWork\Sources\TCMgmtLocalAppStarter.exe";

    // ── App Launcher ──────────────────────────────────────────────────────
    [JsonPropertyName("apps")]
    public List<AppEntry> Apps { get; set; } = [];

    // ── Tray Customizer ───────────────────────────────────────────────────
    /// <summary>Key = AppEntry.Id, Value = show as tray shortcut (legacy compat).</summary>
    [JsonPropertyName("show_in_tray")]
    public Dictionary<string, bool> ShowInTray { get; set; } = new();

    // ── Tray Appearance ───────────────────────────────────────────────────
    /// <summary>
    /// When true the Jiggler toggle item is hidden from the tray context menu.
    /// The jiggler service continues running normally.
    /// </summary>
    [JsonPropertyName("hide_jiggler_in_tray")]
    public bool HideJigglerInTray { get; set; } = false;

    /// <summary>Width in pixels of the teal sidebar in the tray context menu. Default 26.</summary>
    [JsonPropertyName("tray_sidebar_width")]
    public int TraySidebarWidth { get; set; } = 26;

    /// <summary>
    /// Per-type emoji + color overrides. Key = AppEntryType.ToString().
    /// Initialised with built-in presets; user can override in Settings.
    /// </summary>
    [JsonPropertyName("tray_type_configs")]
    public Dictionary<string, TrayTypeConfig> TrayTypeConfigs { get; set; } = TrayTypeDefaults.Build();

    // ── Custom Tools (user-defined) ───────────────────────────────────────
    [JsonPropertyName("custom_tools")]
    public List<CustomToolEntry> CustomTools { get; set; } = [];

    // ── Global Hotkeys (user-customized) ─────────────────────────────────
    [JsonPropertyName("hotkeys")]
    public List<HotkeyConfig> Hotkeys { get; set; } = [];
}
