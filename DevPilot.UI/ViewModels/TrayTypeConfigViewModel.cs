// ============================================================
// DevPilot — IT Automation Hub v1.0
// TrayTypeConfigViewModel.cs — Row VM for Tray Type Appearance editor
// Phase: 13
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Windows.Media;
using DevPilot.Models;

namespace DevPilot.UI.ViewModels;

/// <summary>
/// Editable row for one AppEntryType in the Settings › Tray Type Appearance card.
/// Exposes TypeLabel, Emoji (editable), Color (editable hex), and a live PreviewBrush.
/// </summary>
public sealed class TrayTypeConfigViewModel : BaseViewModel
{
    // ── Identity ──────────────────────────────────────────────────────────

    /// <summary>AppEntryType.ToString() key used when persisting back to AppSettings.</summary>
    public string TypeKey   { get; }

    /// <summary>Human-readable label shown in the Type column.</summary>
    public string TypeLabel { get; }

    // ── Emoji ─────────────────────────────────────────────────────────────

    private string _emoji;
    /// <summary>Single emoji or character shown before the entry name in tray and badge.</summary>
    public string Emoji
    {
        get => _emoji;
        set
        {
            if (SetProperty(ref _emoji, value))
                OnPropertyChanged(nameof(PreviewText));
        }
    }

    // ── Color ─────────────────────────────────────────────────────────────

    private string _color;
    /// <summary>Hex color string, e.g. "#569CD6". Validated live against WPF ColorConverter.</summary>
    public string Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value))
            {
                OnPropertyChanged(nameof(PreviewBrush));
                OnPropertyChanged(nameof(IsValidColor));
            }
        }
    }

    /// <summary>True when <see cref="Color"/> is a valid WPF hex color.</summary>
    public bool IsValidColor
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_color)) return false;  // ← GUARD
            try { _ = (System.Windows.Media.Color)ColorConverter.ConvertFromString(_color); return true; }
            catch { return false; }
        }
    }

    // ── Preview ───────────────────────────────────────────────────────────

    /// <summary>Live SolidColorBrush derived from <see cref="Color"/>; falls back to DimGray.</summary>
    public SolidColorBrush PreviewBrush
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_color))                // ← GUARD
                return new SolidColorBrush(Colors.DimGray);
            try
            {
                var c = (System.Windows.Media.Color)ColorConverter.ConvertFromString(_color);
                return new SolidColorBrush(c);
            }
            catch { return new SolidColorBrush(Colors.DimGray); }
        }
    }

    /// <summary>Combined emoji + type label for the preview pill.</summary>
    public string PreviewText => $"{_emoji}  {TypeLabel}";

    // ── Constructor ───────────────────────────────────────────────────────

    public TrayTypeConfigViewModel(string typeKey, TrayTypeConfig cfg)
    {
        TypeKey   = typeKey;
        TypeLabel = typeKey;
        _emoji    = cfg.Emoji;
        _color    = cfg.Color;
    }

    // ── Export ────────────────────────────────────────────────────────────

    /// <summary>Builds the model snapshot to persist back into AppSettings.</summary>
    public TrayTypeConfig ToModel() => new() { Emoji = _emoji, Color = _color };
}
