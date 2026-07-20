// ============================================================
// DevPilot — IT Automation Hub
// AddAppDialog.xaml.cs — Code-behind for Add/Edit App dialog
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Windows;
using DevPilot.Models;
using Microsoft.Win32;

namespace DevPilot.UI.Views.Dialogs;

public partial class AddAppDialog : Window
{
    public AppEntry? Result { get; private set; }

    private readonly AppEntry _editing;

    public AddAppDialog(AppEntry? existing = null)
    {
        InitializeComponent();
        _editing = existing is not null
            ? CloneEntry(existing)
            : new AppEntry();

        PopulateFields();
    }

    private void PopulateFields()
    {
        TxtName.Text = _editing.Name;
        TxtPath.Text = _editing.Path;
        TxtArgs.Text = _editing.Arguments;
        TxtDescription.Text = _editing.Description;
        TxtCategory.Text = _editing.Category;
        ChkRunAsAdmin.IsChecked = _editing.RunAsAdmin;
        ChkIsVisible.IsChecked = _editing.IsVisible;
        CmbType.SelectedItem = _editing.Type;
    }

    private void CmbType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CmbType.SelectedItem is not AppEntryType type) return;

        string label;
        if (type == AppEntryType.Url)
            label = "URL *  (e.g. https://siemensgpt.siemens.com)";
        else if (type == AppEntryType.ShellCommand)
            label = "Shell Command *  (e.g. ping 8.8.8.8 -t)";
        else if (type == AppEntryType.Ssh)
            label = "SSH Target *  (e.g. user@hostname)";
        else if (type == AppEntryType.Wsl)
            label = "WSL Command *  (e.g. wsl.exe bash -c htop)";
        else if (type == AppEntryType.NetworkShare)
            label = "Network Share *  (e.g. \\\\server\\share)";
        else
            label = "Path / Target *";

        LblPath.Text = label;

        bool hidesBrowse = type == AppEntryType.Url
            || type == AppEntryType.ShellCommand
            || type == AppEntryType.Ssh
            || type == AppEntryType.Wsl;

        BtnBrowse.Visibility = hidesBrowse ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (CmbType.SelectedItem is not AppEntryType type) return;

        if (type == AppEntryType.Folder || type == AppEntryType.NetworkShare)
        {
            // WPF-only folder picker — trick cu OpenFileDialog
            var dlg = new OpenFileDialog
            {
                Title = "Select folder — press Cancel after navigating here",
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Folder",
                Filter = "Folders|*.none",
                ValidateNames = false,
            };
            if (!string.IsNullOrEmpty(TxtPath.Text))
                dlg.InitialDirectory = TxtPath.Text;

            if (dlg.ShowDialog() == true)
                TxtPath.Text = System.IO.Path.GetDirectoryName(dlg.FileName) ?? dlg.FileName;
        }
        else
        {
            string filter;
            if (type == AppEntryType.Executable)
                filter = "Executables|*.exe|All files|*.*";
            else if (type == AppEntryType.PowerShell)
                filter = "PowerShell Scripts|*.ps1|All files|*.*";
            else if (type == AppEntryType.Batch)
                filter = "Batch Files|*.bat;*.cmd|All files|*.*";
            else if (type == AppEntryType.Python)
                filter = "Python Scripts|*.py|All files|*.*";
            else if (type == AppEntryType.VBScript)
                filter = "VBScript Files|*.vbs|All files|*.*";
            else if (type == AppEntryType.Document)
                filter = "Documents|*.xlsx;*.docx;*.pdf;*.txt|All files|*.*";
            else
                filter = "All files|*.*";

            var dlg = new OpenFileDialog { Filter = filter };
            if (!string.IsNullOrEmpty(TxtPath.Text))
                dlg.InitialDirectory = System.IO.Path.GetDirectoryName(TxtPath.Text);
            if (dlg.ShowDialog() == true)
                TxtPath.Text = dlg.FileName;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show("Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(TxtPath.Text))
        {
            MessageBox.Show("Path / Target is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtPath.Focus();
            return;
        }

        _editing.Name = TxtName.Text.Trim();
        _editing.Path = TxtPath.Text.Trim();
        _editing.Arguments = TxtArgs.Text.Trim();
        _editing.Description = TxtDescription.Text.Trim();
        _editing.Category = TxtCategory.Text.Trim();
        _editing.Type = (AppEntryType)(CmbType.SelectedItem ?? AppEntryType.Executable);
        _editing.RunAsAdmin = ChkRunAsAdmin.IsChecked == true;
        _editing.IsVisible = ChkIsVisible.IsChecked == true;

        Result = _editing;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static AppEntry CloneEntry(AppEntry src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        Path = src.Path,
        Arguments = src.Arguments,
        Type = src.Type,
        IsVisible = src.IsVisible,
        RunAsAdmin = src.RunAsAdmin,
        Description = src.Description,
        IconPath = src.IconPath,
        SortOrder = src.SortOrder,
        Category = src.Category,
    };
}
