using System.IO;
using System.Windows;
using DevPilot.Core.Guard;
using Microsoft.Win32;

namespace DevPilot.UI.Services;

/// <summary>Tray actions that check the clipboard or a file before it is pasted into an AI tool.</summary>
public static class PreflightActions
{
    private const long MaxFileBytes = 2 * 1024 * 1024;

    public static void CheckClipboard()
    {
        string? text = null;
        try { text = Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch (System.Runtime.InteropServices.COMException) { }
        if (text is null)
        {
            Notifier.Info("Nothing to check", "The clipboard holds no text (or another program is using it).");
            return;
        }
        Show("clipboard", TextPreflight.Check(text, LocalPolicy.Load()));
    }

    public static void CheckFile()
    {
        var dlg = new OpenFileDialog { Title = "Check a text file before pasting it into an AI tool", Filter = "Text files|*.txt;*.md;*.csv;*.json;*.log;*.xml;*.yml;*.yaml;*.cs;*.py;*.ps1;*.bat;*.cmd|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            if (new FileInfo(dlg.FileName).Length > MaxFileBytes)
            {
                Notifier.Error("File too large", "Only files up to 2 MB are checked.");
                return;
            }
            Show(Path.GetFileName(dlg.FileName), TextPreflight.Check(File.ReadAllText(dlg.FileName), LocalPolicy.Load()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notifier.Error("Could not read the file", ex.Message);
        }
    }

    private static void Show(string what, PreflightResult result) =>
        MessageBox.Show(result.Describe(), $"DevPilot · check before pasting ({what})", MessageBoxButton.OK,
            result.IsClean ? MessageBoxImage.Information : MessageBoxImage.Warning);
}
