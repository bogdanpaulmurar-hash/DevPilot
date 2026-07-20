// ============================================================
// DevPilot — IT Automation Hub v1.0
// HotkeyPickerDialog.xaml.cs — Predefined hotkey picker
// Phase: 10C
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace DevPilot.UI.Views.Dialogs;

public partial class HotkeyPickerDialog : Window
{
    // ── Predefined key list ───────────────────────────────────────────────
    private static readonly IReadOnlyList<string> AvailableKeys =
    [
        "A","B","C","D","E","F","G","H","I","J","K","L","M",
        "N","O","P","Q","R","S","T","U","V","W","X","Y","Z",
        "0","1","2","3","4","5","6","7","8","9",
        "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
        "Space","Home","End","PageUp","PageDown",
        "Insert","Delete","Left","Right","Up","Down"
    ];

    // ── Result ────────────────────────────────────────────────────────────
    /// <summary>Set when the user clicks Apply; null if cancelled.</summary>
    public (string Modifiers, string Key)? Result { get; private set; }

    // ── Constructor ───────────────────────────────────────────────────────
    public HotkeyPickerDialog(string actionLabel, string currentModifiers, string currentKey)
    {
        InitializeComponent();

        TxtActionLabel.Text = $"Assign hotkey for:  {actionLabel}";

        // Populate key ComboBox
        CmbKey.ItemsSource = AvailableKeys;

        // Pre-select current modifiers
        var mods = currentModifiers.Split('+', System.StringSplitOptions.RemoveEmptyEntries);
        ChkCtrl.IsChecked  = Contains(mods, "Ctrl");
        ChkAlt.IsChecked   = Contains(mods, "Alt");
        ChkShift.IsChecked = Contains(mods, "Shift");
        ChkWin.IsChecked   = Contains(mods, "Win");

        // Pre-select current key
        var idx = ((IReadOnlyList<string>)AvailableKeys)
                  .ToList().FindIndex(k => string.Equals(k, currentKey,
                                          System.StringComparison.OrdinalIgnoreCase));
        CmbKey.SelectedIndex = idx >= 0 ? idx : 0;

        UpdatePreview();
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static bool Contains(string[] arr, string val) =>
        System.Array.Exists(arr, x => string.Equals(x, val, System.StringComparison.OrdinalIgnoreCase));

    private string BuildModifiers()
    {
        var parts = new List<string>(4);
        if (ChkCtrl.IsChecked  == true) parts.Add("Ctrl");
        if (ChkAlt.IsChecked   == true) parts.Add("Alt");
        if (ChkShift.IsChecked == true) parts.Add("Shift");
        if (ChkWin.IsChecked   == true) parts.Add("Win");
        return string.Join("+", parts);
    }

    private void UpdatePreview()
    {
        var mods = BuildModifiers();
        var key  = CmbKey.SelectedItem as string ?? string.Empty;
        var noMod = string.IsNullOrEmpty(mods);

        TxtWarning.Visibility = noMod ? Visibility.Visible : Visibility.Collapsed;
        BtnApply.IsEnabled    = !noMod && !string.IsNullOrEmpty(key);
        TxtPreview.Text       = noMod ? "(no modifier)" : $"{mods}+{key}";
    }

    // ── Event handlers ────────────────────────────────────────────────────
    private void OnModifierChanged(object sender, RoutedEventArgs e) => UpdatePreview();
    private void OnKeyChanged(object sender, SelectionChangedEventArgs e)  => UpdatePreview();

    private void OnApply(object sender, RoutedEventArgs e)
    {
        var mods = BuildModifiers();
        var key  = CmbKey.SelectedItem as string ?? string.Empty;
        if (string.IsNullOrEmpty(mods) || string.IsNullOrEmpty(key)) return;

        Result       = (mods, key);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}