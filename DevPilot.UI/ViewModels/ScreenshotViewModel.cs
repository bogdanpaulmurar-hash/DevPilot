// ============================================================
// DevPilot — IT Automation Hub v1.0
// ScreenshotViewModel.cs — ViewModel pentru Screenshot Tab
// Phase: 7
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using DevPilot.Core.Interfaces;
using DevPilot.Models;
using DevPilot.UI.Commands;

namespace DevPilot.UI.ViewModels;

public sealed class ScreenshotViewModel : BaseViewModel
{
    private readonly IScreenshotService _screenshotService;

    // ── Properties ────────────────────────────────────────────────────────
    private string _statusText = "Gata.";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private string _outputFolder = string.Empty;
    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (SetProperty(ref _outputFolder, value))
                _screenshotService.OutputFolder = value;
        }
    }

    private ScreenshotEntry? _selectedEntry;
    public ScreenshotEntry? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public ObservableCollection<ScreenshotEntry> History { get; } = [];

    // ── Commands ──────────────────────────────────────────────────────────
    public ICommand CaptureFullScreenCommand    { get; }
    public ICommand CaptureActiveWindowCommand  { get; }
    public ICommand CopyToClipboardCommand      { get; }
    public ICommand OpenFolderCommand           { get; }
    public ICommand OpenAnnotationEditorCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────
    public ScreenshotViewModel(IScreenshotService screenshotService)
    {
        _screenshotService = screenshotService;
        _outputFolder      = screenshotService.OutputFolder;

        CaptureFullScreenCommand    = new RelayCommand(async _ => await DoCaptureFullAsync());
        CaptureActiveWindowCommand  = new RelayCommand(async _ => await DoCaptureWindowAsync());
        CopyToClipboardCommand      = new RelayCommand(_ => DoCopyToClipboard());
        OpenFolderCommand           = new RelayCommand(_ => DoOpenFolder());
        OpenAnnotationEditorCommand = new RelayCommand(_ => DoOpenAnnotationEditor(), _ => SelectedEntry != null);
    }

    // ── Handlers ──────────────────────────────────────────────────────────

    private async Task DoCaptureFullAsync()
    {
        StatusText = "Capturare ecran complet...";
        var path = await _screenshotService.CaptureFullScreenAsync();
        RefreshHistory();
        StatusText = string.IsNullOrEmpty(path)
            ? "Eroare la captura!"
            : $"Salvat: {Path.GetFileName(path)}";
    }

    private async Task DoCaptureWindowAsync()
    {
        StatusText = "Capturare fereastra activa...";
        var path = await _screenshotService.CaptureActiveWindowAsync();
        RefreshHistory();
        StatusText = string.IsNullOrEmpty(path)
            ? "Eroare la captura!"
            : $"Salvat: {Path.GetFileName(path)}";
    }

    private void DoCopyToClipboard()
    {
        _screenshotService.CopyLastToClipboard();
        StatusText = "Screenshot copiat in clipboard.";
    }

    private void DoOpenFolder()
    {
        var folder = _screenshotService.OutputFolder;
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start("explorer.exe", folder);
    }

    private void DoOpenAnnotationEditor()
    {
        if (SelectedEntry == null) return;
        var editor = new Views.Dialogs.AnnotationEditorWindow(SelectedEntry.FilePath);
        editor.Show();
    }

    internal void RefreshHistory()
    {
        History.Clear();
        foreach (var entry in _screenshotService.History)
            History.Add(entry);
    }

    /// <summary>Apelat din MainWindow cand hotkey-ul Ctrl+Shift+S este apasat.</summary>
    public async Task TriggerFullScreenAsync()
    {
        await DoCaptureFullAsync();
    }
}
