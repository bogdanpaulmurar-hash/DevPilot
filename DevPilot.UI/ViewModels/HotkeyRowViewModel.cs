// ============================================================
// DevPilot — IT Automation Hub v1.0
// HotkeyRowViewModel.cs — Row VM for the Hotkeys table in Settings
// Phase: 10C (+IsEnabled, +ResetCommand, +default-values for reset)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Windows.Input;
using DevPilot.UI.Commands;

namespace DevPilot.UI.ViewModels;

/// <summary>One row in the Settings › Hotkeys table.</summary>
public sealed class HotkeyRowViewModel : BaseViewModel
{
    // ── Identity ──────────────────────────────────────────────────────────
    /// <summary>Win32 RegisterHotKey id.</summary>
    public int    Id          { get; }
    /// <summary>Internal action key, e.g. "ShowHide".</summary>
    public string ActionKey   { get; }
    /// <summary>Human-readable label shown in the table.</summary>
    public string DisplayName { get; }

    // ── Factory defaults (used by ResetCommand) ───────────────────────────
    private readonly string _defaultModifiers;
    private readonly string _defaultKey;

    // ── Modifiers ─────────────────────────────────────────────────────────
    private string _modifiers;
    /// <summary>Modifier string, e.g. "Ctrl+Shift".</summary>
    public string Modifiers
    {
        get => _modifiers;
        set { if (SetProperty(ref _modifiers, value)) OnPropertyChanged(nameof(HotkeyDisplay)); }
    }

    // ── Key ───────────────────────────────────────────────────────────────
    private string _key;
    /// <summary>Key string, e.g. "D".</summary>
    public string Key
    {
        get => _key;
        set { if (SetProperty(ref _key, value)) OnPropertyChanged(nameof(HotkeyDisplay)); }
    }

    /// <summary>Formatted display string shown in the table, e.g. "Ctrl+Shift+D".</summary>
    public string HotkeyDisplay => $"{Modifiers}+{Key}";

    // ── Enabled ───────────────────────────────────────────────────────────
    private bool _isEnabled;
    /// <summary>When false the hotkey is not registered with Win32.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    // ── Commands ──────────────────────────────────────────────────────────
    public ICommand ChangeCommand { get; }
    public ICommand ResetCommand  { get; }

    /// <summary>Async factory injected by SettingsViewModel — returns (Mods, Key) or null.</summary>
    internal Func<Task<(string Modifiers, string Key)?>>? OnChangeRequested { get; set; }

    // ── Constructor ───────────────────────────────────────────────────────
    public HotkeyRowViewModel(
        int    id,
        string actionKey,
        string displayName,
        string modifiers,
        string key,
        bool   isEnabled        = true,
        string defaultModifiers = "",
        string defaultKey       = "")
    {
        Id          = id;
        ActionKey   = actionKey;
        DisplayName = displayName;
        _modifiers  = modifiers;
        _key        = key;
        _isEnabled  = isEnabled;

        _defaultModifiers = string.IsNullOrEmpty(defaultModifiers) ? modifiers : defaultModifiers;
        _defaultKey       = string.IsNullOrEmpty(defaultKey)       ? key       : defaultKey;

        ChangeCommand = new RelayCommand(async _ =>
        {
            if (OnChangeRequested is null) return;
            var result = await OnChangeRequested();
            if (result.HasValue)
            {
                Modifiers = result.Value.Modifiers;
                Key       = result.Value.Key;
            }
        });

        ResetCommand = new RelayCommand(_ =>
        {
            Modifiers = _defaultModifiers;
            Key       = _defaultKey;
            IsEnabled = true;
        });
    }
}