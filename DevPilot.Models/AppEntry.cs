// ============================================================
// DevPilot — IT Automation Hub v1.0
// AppEntry.cs — Model for a single App Launcher item
// Phase: 12 (+ShowInTray per-entry property)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Text.Json.Serialization;

namespace DevPilot.Models;

/// <summary>
/// Represents one configurable entry in the App Launcher panel.
/// Persisted as part of AppSettings.Apps list in config.json.
/// </summary>
public class AppEntry
{
    /// <summary>Unique identifier (GUID string)</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Display name shown in the launcher list</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Target path, URL, command, or script path depending on Type.
    /// Examples:
    ///   Executable   → C:\Tools\app.exe
    ///   Folder       → C:\Users\z003fu5c\Documents
    ///   Url          → https://siemensgpt.siemens.com
    ///   PowerShell   → C:\Scripts\deploy.ps1
    ///   Batch        → C:\Scripts\run.bat
    ///   Python       → C:\Scripts\tool.py
    ///   ShellCommand → ping 8.8.8.8 -t
    ///   Document     → C:\Docs\report.xlsx
    ///   NetworkShare → \\server\share
    ///   Ssh          → user@hostname
    ///   Wsl          → bash -c "htop"
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Optional command-line arguments (for Executable, PowerShell, Python, etc.)</summary>
    [JsonPropertyName("args")]
    public string Arguments { get; set; } = string.Empty;

    /// <summary>Type determines how LauncherService launches this entry</summary>
    [JsonPropertyName("type")]
    public AppEntryType Type { get; set; } = AppEntryType.Executable;

    /// <summary>Whether this entry is visible in the launcher list</summary>
    [JsonPropertyName("is_visible")]
    public bool IsVisible { get; set; } = true;

    /// <summary>Whether to run as Administrator (ShellExecute runas)</summary>
    [JsonPropertyName("run_as_admin")]
    public bool RunAsAdmin { get; set; } = false;

    /// <summary>Optional description / tooltip shown in UI</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional icon path (.ico or .exe for extracted icon)</summary>
    [JsonPropertyName("icon_path")]
    public string IconPath { get; set; } = string.Empty;

    /// <summary>Display order in the launcher list (lower = higher)</summary>
    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; } = 0;

    /// <summary>Optional category/group tag for filtering</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    // ── Phase 16 — per-app font style in tray ─────────────────────────────────
    [JsonPropertyName("tray_font_style")]
    public string TrayFontStyle { get; set; } = "Normal";   

    // ── Phase 12 ──────────────────────────────────────────────────────────

    /// <summary>
    /// Whether this entry appears as a shortcut in the system tray context menu.
    /// Persisted directly on the AppEntry inside the "apps" array in config.json.
    /// Source of truth for TrayService — max 20 entries with ShowInTray == true are shown.
    /// </summary>
    [JsonPropertyName("show_in_tray")]
    public bool ShowInTray { get; set; } = false;

    // ── Phase 14 — per-app colour / emoji overrides ───────────────────────

    /// <summary>
    /// Optional emoji that overrides the type-level emoji for this specific entry
    /// in both the Settings Tray Customizer and the tray context menu.
    /// Leave empty to inherit the type default.
    /// </summary>
    [JsonPropertyName("tag_emoji")]
    public string TagEmoji { get; set; } = string.Empty;

    /// <summary>
    /// Optional #RRGGBB colour that overrides the type-level colour for this entry.
    /// Leave empty to inherit the type default.
    /// </summary>
    [JsonPropertyName("tag_color")]
    public string TagColor { get; set; } = string.Empty;
}
