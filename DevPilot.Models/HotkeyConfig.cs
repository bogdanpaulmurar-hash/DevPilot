// ============================================================
// DevPilot — IT Automation Hub v1.0
// HotkeyConfig.cs — Model pentru Global Hotkeys
// Autor: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

namespace DevPilot.Models;

public class HotkeyConfig
{
    /// <summary>Hotkey ID (Win32 RegisterHotKey id)</summary>
    public int    Id          { get; set; }
    public string Action      { get; set; } = string.Empty;
    public string Modifiers   { get; set; } = "Ctrl+Shift"; // e.g. "Ctrl+Shift"
    public string Key         { get; set; } = "D";          // e.g. "D"
    public bool   IsEnabled   { get; set; } = true;
}
