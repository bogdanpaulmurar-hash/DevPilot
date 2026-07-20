// ============================================================
// DevPilot — IT Automation Hub
// AddToolDialog.xaml.cs — Code-behind for Add Custom Tool dialog
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.IO;
using System.Windows;
using Microsoft.Win32;
using DevPilot.Models;

namespace DevPilot.UI.Views.Dialogs;

/// <summary>
/// Modal dialog for creating a new <see cref="DevPilot.Models.CustomToolEntry"/>.
/// Inspect <see cref="ResultName"/>, <see cref="ResultDescription"/>,
/// <see cref="ResultIcon"/>, and <see cref="ResultExePath"/> when
/// <see cref="Window.DialogResult"/> is <c>true</c>.
/// </summary>
public partial class AddToolDialog : Window
{
    /// <summary>Validated tool name entered by the user.</summary>
    public string ResultName        { get; private set; } = string.Empty;

    /// <summary>Optional description entered by the user.</summary>
    public string ResultDescription { get; private set; } = string.Empty;

    /// <summary>Emoji icon entered by the user (defaults to 🔧).</summary>
    public string ResultIcon        { get; private set; } = "🔧";

    /// <summary>Validated executable path entered by the user.</summary>
    public string ResultExePath     { get; private set; } = string.Empty;

    /// <summary>Initialises the dialog with default values.</summary>
    public AddToolDialog() => InitializeComponent();

    /// <summary>Initialises the dialog in edit mode with pre-filled values.</summary>
    public AddToolDialog(CustomToolEntry existing) : this()
    {
        Title               = "Edit Custom Tool";
        TxtName.Text        = existing.Name;
        TxtDescription.Text = existing.Description;
        TxtIcon.Text        = existing.Icon;
        TxtExePath.Text     = existing.ExePath;
        BtnAction.Content   = "💾  Save Changes";
    }

    // ── Event handlers ────────────────────────────────────────────────────

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select executable",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
        };

        if (dlg.ShowDialog() == true)
            TxtExePath.Text = dlg.FileName;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var name    = TxtName.Text.Trim();
        var exePath = TxtExePath.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Tool Name is required.");
            TxtName.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(exePath))
        {
            ShowError("Executable Path is required.");
            TxtExePath.Focus();
            return;
        }

        ResultName        = name;
        ResultDescription = TxtDescription.Text.Trim();
        ResultIcon        = string.IsNullOrWhiteSpace(TxtIcon.Text) ? "🔧" : TxtIcon.Text.Trim();
        ResultExePath     = exePath;

        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void ShowError(string message)
    {
        TxtError.Text       = message;
        TxtError.Visibility = Visibility.Visible;
    }
}