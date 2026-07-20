// ============================================================
// DevPilot — IT Automation Hub v1.0
// InputBoxDialog.xaml.cs — Hotkey text input dialog
// Phase: 9
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Windows;
using System.Windows.Input;

namespace DevPilot.UI.Views.Dialogs;

/// <summary>
/// Modal dialog that prompts the user for a hotkey string such as "Ctrl+Shift+D"
/// and parses the result into (Modifiers, Key).
/// </summary>
public partial class InputBoxDialog : Window
{
    /// <summary>Parsed modifiers string (e.g. "Ctrl+Shift"). Null if cancelled.</summary>
    public string? ResultModifiers { get; private set; }

    /// <summary>Parsed key string (e.g. "D"). Null if cancelled.</summary>
    public string? ResultKey { get; private set; }

    public InputBoxDialog(string prompt, string currentValue)
    {
        InitializeComponent();
        TxtPrompt.Text = prompt;
        TxtInput.Text  = currentValue;
        Loaded += (_, _) => { TxtInput.SelectAll(); TxtInput.Focus(); };
    }

    private void OnOk(object sender, RoutedEventArgs e)     => TryAccept();
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Return) { TryAccept(); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }

    private void TryAccept()
    {
        var raw = TxtInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            MessageBox.Show("Please enter a hotkey string (e.g. Ctrl+Shift+D).",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var parts = raw.Split('+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            MessageBox.Show("Format must be Modifiers+Key  (e.g. Ctrl+Shift+D).",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultKey       = parts[^1];
        ResultModifiers = string.Join("+", parts[..^1]);
        DialogResult    = true;
    }
}