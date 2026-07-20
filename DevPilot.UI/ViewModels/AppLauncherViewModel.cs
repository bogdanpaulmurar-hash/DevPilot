// ============================================================
// DevPilot — IT Automation Hub
// AppLauncherViewModel.cs — ViewModel for App Launcher tab
// Phase: 15 (Fix: RaiseCanExecuteChanged on SelectedApp change)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using DevPilot.Core.Interfaces;
using DevPilot.Models;
using DevPilot.UI.Commands;
using Microsoft.Extensions.Logging;

namespace DevPilot.UI.ViewModels;

public class AppLauncherViewModel : BaseViewModel
{
    private readonly ILauncherService _launcher;
    private readonly ISettingsManager _settings;
    private readonly ILogger<AppLauncherViewModel> _logger;

    public ObservableCollection<AppEntry> Apps { get; } = [];

    // ── SelectedApp ───────────────────────────────────────────────────
    private AppEntry? _selectedApp;
    public AppEntry? SelectedApp
    {
        get => _selectedApp;
        set
        {
            _selectedApp = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedEntry));

            // Re-evaluate CanExecute for every selection-dependent command.
            // RelayCommand uses a custom CanExecuteChanged event (not routed through
            // CommandManager.RequerySuggested), so we must raise it explicitly here.
            ((RelayCommand)LaunchCommand).RaiseCanExecuteChanged();
            ((RelayCommand)EditCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
            ((RelayCommand)MoveUpCommand).RaiseCanExecuteChanged();
            ((RelayCommand)MoveDownCommand).RaiseCanExecuteChanged();
        }
    }

    // ── StatusText ────────────────────────────────────────────────────
    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusMessage)); }
    }

    // ── FilterText ────────────────────────────────────────────────────
    private string _filterText = string.Empty;
    public string FilterText
    {
        get => _filterText;
        set { _filterText = value; OnPropertyChanged(); OnPropertyChanged(nameof(FilteredEntries)); }
    }

    // ── Alias properties pentru compatibilitate XAML ──────────────────
    public IEnumerable<AppEntry> FilteredEntries =>
        string.IsNullOrWhiteSpace(FilterText)
            ? Apps
            : Apps.Where(a => a.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

    public AppEntry? SelectedEntry
    {
        get => SelectedApp;
        set { SelectedApp = value; OnPropertyChanged(); }
    }

    public ICommand RemoveCommand => DeleteCommand;

    public string StatusMessage => StatusText;

    // ── Dialog factory — injected from MainWindow code-behind ─────────
    public Func<AppEntry?, Task<AppEntry?>>? ShowAddEditDialog { get; set; }

    // ── Commands ──────────────────────────────────────────────────────
    public ICommand LaunchCommand   { get; }
    public ICommand AddCommand      { get; }
    public ICommand EditCommand     { get; }
    public ICommand DeleteCommand   { get; }
    public ICommand MoveUpCommand   { get; }
    public ICommand MoveDownCommand { get; }

    public AppLauncherViewModel(
        ILauncherService launcher,
        ISettingsManager settings,
        ILogger<AppLauncherViewModel> logger)
    {
        _launcher = launcher;
        _settings = settings;
        _logger   = logger;

        LaunchCommand   = new RelayCommand(_ => LaunchAsync(),  _ => SelectedApp is not null);
        AddCommand      = new RelayCommand(_ => AddAsync());
        EditCommand     = new RelayCommand(_ => EditAsync(),    _ => SelectedApp is not null);
        DeleteCommand   = new RelayCommand(_ => Delete(),       _ => SelectedApp is not null);
        MoveUpCommand   = new RelayCommand(_ => MoveUp(),       _ => CanMoveUp());
        MoveDownCommand = new RelayCommand(_ => MoveDown(),     _ => CanMoveDown());

        LoadApps();
    }

    // ── Load ──────────────────────────────────────────────────────────

    private void LoadApps()
    {
        Apps.Clear();
        foreach (var app in _settings.Current.Apps)
            Apps.Add(app);
    }

    // ── Launch ────────────────────────────────────────────────────────

    private async void LaunchAsync()
    {
        if (SelectedApp is null) return;
        try
        {
            StatusText = $"Launching: {SelectedApp.Name}…";
            await _launcher.LaunchAsync(SelectedApp);
            StatusText = $"Launched: {SelectedApp.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _logger.LogError(ex, "Launch failed for {App}", SelectedApp.Name);
        }
    }

    // ── Add ───────────────────────────────────────────────────────────

    private async void AddAsync()
    {
        if (ShowAddEditDialog is null) return;
        var result = await ShowAddEditDialog(null);
        if (result is null) return;

        _settings.Current.Apps.Add(result);
        Apps.Add(result);
        _settings.Save();
        StatusText = $"Added: {result.Name}";
        OnPropertyChanged(nameof(FilteredEntries));
    }

    // ── Edit ──────────────────────────────────────────────────────────

    private async void EditAsync()
    {
        if (SelectedApp is null || ShowAddEditDialog is null) return;
        var result = await ShowAddEditDialog(SelectedApp);
        if (result is null) return;

        var idx = Apps.IndexOf(SelectedApp);
        if (idx < 0) return;

        _settings.Current.Apps[idx] = result;
        Apps[idx] = result;
        _settings.Save();
        StatusText = $"Updated: {result.Name}";
        OnPropertyChanged(nameof(FilteredEntries));
    }

    // ── Delete ────────────────────────────────────────────────────────

    private void Delete()
    {
        if (SelectedApp is null) return;
        _settings.Current.Apps.Remove(SelectedApp);
        Apps.Remove(SelectedApp);
        _settings.Save();
        StatusText = "Entry deleted.";
        OnPropertyChanged(nameof(FilteredEntries));
    }

    // ── Move ──────────────────────────────────────────────────────────

    private bool CanMoveUp()   => SelectedApp is not null && Apps.IndexOf(SelectedApp) > 0;
    private bool CanMoveDown() => SelectedApp is not null && Apps.IndexOf(SelectedApp) < Apps.Count - 1;

    private void MoveUp()
    {
        if (SelectedApp is null) return;
        var idx = Apps.IndexOf(SelectedApp);
        if (idx <= 0) return;
        Apps.Move(idx, idx - 1);
        var item = _settings.Current.Apps[idx];
        _settings.Current.Apps.RemoveAt(idx);
        _settings.Current.Apps.Insert(idx - 1, item);
        _settings.Save();
        // Re-evaluate move commands after position change
        ((RelayCommand)MoveUpCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveDownCommand).RaiseCanExecuteChanged();
    }

    private void MoveDown()
    {
        if (SelectedApp is null) return;
        var idx = Apps.IndexOf(SelectedApp);
        if (idx >= Apps.Count - 1) return;
        Apps.Move(idx, idx + 1);
        var item = _settings.Current.Apps[idx];
        _settings.Current.Apps.RemoveAt(idx);
        _settings.Current.Apps.Insert(idx + 1, item);
        _settings.Save();
        // Re-evaluate move commands after position change
        ((RelayCommand)MoveUpCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveDownCommand).RaiseCanExecuteChanged();
    }
}
