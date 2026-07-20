// ============================================================
// DevPilot — IT Automation Hub v1.0
// TrayAppEntryViewModel.cs — Row VM for Tray Customizer in Settings
// Phase: 16 (+TrayFontStyle: Normal/Bold/SemiBold/Italic/Strikethrough)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Windows.Input;
using System.Windows.Media;
using DevPilot.Models;
using DevPilot.UI.Commands;

namespace DevPilot.UI.ViewModels;

/// <summary>
/// Represents one App Launcher entry in the Settings › Tray Customizer list.
/// Exposes ShowInTray, type-level badge defaults, per-app emoji/color overrides,
/// and per-app font style (Normal/Bold/SemiBold/Italic/Strikethrough).
/// </summary>
public sealed class TrayAppEntryViewModel : BaseViewModel
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>AppEntry.Id — used as key when persisting back to AppEntry.</summary>
    public string AppId { get; }

    /// <summary>Display name of the app entry.</summary>
    public string Name { get; }

    /// <summary>Human-readable entry type (e.g. "Executable", "Url", "PowerShell").</summary>
    public string TypeLabel { get; }

    // ── ShowInTray ────────────────────────────────────────────────────────────

    private bool _showInTray;
    /// <summary>Whether this entry should appear in the system tray context menu.</summary>
    public bool ShowInTray
    {
        get => _showInTray;
        set => SetProperty(ref _showInTray, value);
    }

    // ── Type-level badge (fallback) ───────────────────────────────────────────

    private string _typeEmoji = string.Empty;
    /// <summary>Emoji from the type-level preset — used when TagEmoji is empty.</summary>
    public string TypeEmoji
    {
        get => _typeEmoji;
        set
        {
            if (SetProperty(ref _typeEmoji, value))
                OnPropertyChanged(nameof(EffectiveEmoji));
        }
    }

    private string _typeColor = "#555555";
    /// <summary>Hex color from the type-level preset — used when TagColor is empty.</summary>
    public string TypeColor
    {
        get => _typeColor;
        set
        {
            if (SetProperty(ref _typeColor, value))
            {
                OnPropertyChanged(nameof(TypeBrush));
                OnPropertyChanged(nameof(EffectiveBrush));
            }
        }
    }

    /// <summary>Brush built from TypeColor.</summary>
    public SolidColorBrush TypeBrush => ParseBrush(_typeColor)
                                        ?? new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

    // ── Per-app overrides ─────────────────────────────────────────────────────

    private string _tagEmoji = string.Empty;
    /// <summary>
    /// Optional emoji override for this entry in the tray menu.
    /// Leave empty to inherit the type default.
    /// </summary>
    public string TagEmoji
    {
        get => _tagEmoji;
        set
        {
            if (SetProperty(ref _tagEmoji, value))
                OnPropertyChanged(nameof(EffectiveEmoji));
        }
    }

    private string _tagColor = string.Empty;
    /// <summary>
    /// Optional #RRGGBB colour override for this entry.
    /// Leave empty to use the type-level color.
    /// </summary>
    public string TagColor
    {
        get => _tagColor;
        set
        {
            if (SetProperty(ref _tagColor, value))
            {
                OnPropertyChanged(nameof(IsTagColorValid));
                OnPropertyChanged(nameof(EffectiveBrush));
            }
        }
    }

    /// <summary>True when TagColor is empty (inheriting) OR is a valid hex color.</summary>
    public bool IsTagColorValid =>
        string.IsNullOrWhiteSpace(_tagColor) || ParseBrush(_tagColor) is not null;

    // ── Phase 16 — TrayFontStyle ──────────────────────────────────────────────

    private string _trayFontStyle = "Normal";
    /// <summary>
    /// Font style applied to this entry in the tray menu.
    /// Values: "Normal", "Bold", "SemiBold", "Italic", "Strikethrough"
    /// </summary>
    public string TrayFontStyle
    {
        get => _trayFontStyle;
        set
        {
            if (SetProperty(ref _trayFontStyle, value))
            {
                OnPropertyChanged(nameof(IsStyleNormal));
                OnPropertyChanged(nameof(IsStyleBold));
                OnPropertyChanged(nameof(IsStyleSemiBold));
                OnPropertyChanged(nameof(IsStyleItalic));
                OnPropertyChanged(nameof(IsStyleStrikethrough));
            }
        }
    }

    // Helpers pentru buton Foreground binding (via BoolToAccentBrushConverter)
    public bool IsStyleNormal => _trayFontStyle == "Normal";
    public bool IsStyleBold => _trayFontStyle == "Bold";
    public bool IsStyleSemiBold => _trayFontStyle == "SemiBold";
    public bool IsStyleItalic => _trayFontStyle == "Italic";
    public bool IsStyleStrikethrough => _trayFontStyle == "Strikethrough";

    // Comenzi pentru butoanele de stil
    public ICommand SetNormalCommand { get; }
    public ICommand SetBoldCommand { get; }
    public ICommand SetSemiBoldCommand { get; }
    public ICommand SetItalicCommand { get; }
    public ICommand SetStrikethroughCommand { get; }

    // ── Effective resolved values ─────────────────────────────────────────────

    /// <summary>Emoji to show: TagEmoji if set, else TypeEmoji.</summary>
    public string EffectiveEmoji =>
        string.IsNullOrWhiteSpace(_tagEmoji) ? _typeEmoji : _tagEmoji;

    /// <summary>Badge brush: TagColor if valid and set, else TypeColor brush.</summary>
    public SolidColorBrush EffectiveBrush
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_tagColor))
            {
                var b = ParseBrush(_tagColor);
                if (b is not null) return b;
            }
            return TypeBrush;
        }
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public TrayAppEntryViewModel(
        string appId,
        string name,
        AppEntryType type,
        bool showInTray,
        string typeEmoji = "",
        string typeColor = "#555555",
        string tagEmoji = "",
        string tagColor = "",
        string trayFontStyle = "Normal")
    {
        AppId = appId;
        Name = name;
        TypeLabel = type.ToString();
        _showInTray = showInTray;
        _typeEmoji = typeEmoji;      // ✅ FIX: câmpuri private, nu parametri
        _typeColor = typeColor;      // ✅ FIX
        _tagEmoji = tagEmoji;       // ✅ FIX
        _tagColor = tagColor;       // ✅ FIX
        _trayFontStyle = trayFontStyle;  // ✅ FIX

        // Action<object?> — parametrul _ este obligatoriu in RelayCommand
        SetNormalCommand = new RelayCommand(_ => TrayFontStyle = "Normal");
        SetBoldCommand = new RelayCommand(_ => TrayFontStyle = "Bold");
        SetSemiBoldCommand = new RelayCommand(_ => TrayFontStyle = "SemiBold");
        SetItalicCommand = new RelayCommand(_ => TrayFontStyle = "Italic");
        SetStrikethroughCommand = new RelayCommand(_ => TrayFontStyle = "Strikethrough");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SolidColorBrush? ParseBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var h = hex.Trim();
        // Un hex valid are minim 4 caractere: #RGB
        if (h.StartsWith('#') && h.Length < 4) return null;
        try
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(h);
            return new SolidColorBrush(c);
        }
        catch
        {
            return null;
        }
    }
}