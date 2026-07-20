// ============================================================
// DevPilot — IT Automation Hub
// CustomToolViewModel.cs — Item VM for one user-defined tool
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Diagnostics;
using System.IO;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using DevPilot.Models;
using DevPilot.UI.Commands;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace DevPilot.UI.ViewModels;

/// <summary>
/// Item-level ViewModel wrapping one <see cref="CustomToolEntry"/>.
/// Owns its Process handle and 2-second polling timer.
/// </summary>
public sealed class CustomToolViewModel : BaseViewModel, IDisposable
{
    private readonly ILogger _logger;
    private Process?  _process;
    private readonly Timer _timer = new(2000) { AutoReset = true };
    private bool _disposed;

    /// <summary>The backing data model (persisted in config.json).</summary>
    public CustomToolEntry Entry { get; }

    // ── Status ────────────────────────────────────────────────────────────

    private string _statusText = "● Not running";
    /// <summary>Live status label shown in the card.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private bool _isRunning;
    /// <summary>True while the process is alive — used by DataTrigger for green color.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    private string _warning = string.Empty;
    /// <summary>Inline warning text shown when exe path is missing.</summary>
    public string Warning
    {
        get => _warning;
        set
        {
            if (SetProperty(ref _warning, value))
                OnPropertyChanged(nameof(WarningVisibility));
        }
    }

    /// <summary>Collapses the warning banner when <see cref="Warning"/> is empty.</summary>
    public Visibility WarningVisibility =>
        string.IsNullOrEmpty(_warning) ? Visibility.Collapsed : Visibility.Visible;

    // ── Commands ──────────────────────────────────────────────────────────

    /// <summary>Starts the tool executable.</summary>
    public ICommand LaunchCommand     { get; }

    /// <summary>Opens the exe directory in Windows Explorer.</summary>
    public ICommand OpenFolderCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────

    /// <summary>Creates an item ViewModel for the given <paramref name="entry"/>.</summary>
    public CustomToolViewModel(CustomToolEntry entry, ILogger logger)
    {
        Entry   = entry;
        _logger = logger;

        LaunchCommand     = new RelayCommand(_ => Launch());
        OpenFolderCommand = new RelayCommand(_ => OpenFolder());

        _timer.Elapsed += (_, _) => Poll();

        RefreshWarning();
    }

    // ── Public ────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-checks File.Exists for the exe path.
    /// Call after the user edits paths in Settings.
    /// </summary>
    public void RefreshWarning()
    {
        Warning = File.Exists(Entry.ExePath)
            ? string.Empty
            : "⚠  Executable not found. Edit the path and save.";
    }

    /// <summary>
    /// Updates the entry fields in-place and notifies the UI to re-render the card.
    /// Call this after the user confirms the edit dialog.
    /// </summary>
    public void UpdateEntry(string name, string description, string icon, string exePath)
    {
        Entry.Name        = name;
        Entry.Description = description;
        Entry.Icon        = icon;
        Entry.ExePath     = exePath;
        OnPropertyChanged(nameof(Entry)); // re-evaluates all Entry.* bindings
        RefreshWarning();
    }

    // ── Private ───────────────────────────────────────────────────────────

    private void Launch()
    {
        if (!File.Exists(Entry.ExePath))
        {
            Warning    = "⚠  Executable not found. Edit the path and save.";
            StatusText = "⚠  Not configured.";
            _logger.LogWarning("Custom tool exe not found: {Path}", Entry.ExePath);
            return;
        }

        try
        {
            _process   = Process.Start(new ProcessStartInfo(Entry.ExePath) { UseShellExecute = true });
            Warning    = string.Empty;
            StatusText = $"● Running (PID: {_process?.Id})";
            IsRunning  = _process is not null;

            if (_process is not null)
            {
                _timer.Start();
                _logger.LogInformation("{Name} launched. PID={Pid}", Entry.Name, _process.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Custom tool {Name} launch failed.", Entry.Name);
            StatusText = $"⚠  Launch failed: {ex.Message}";
            IsRunning  = false;
        }
    }

    private void OpenFolder()
    {
        var dir = Path.GetDirectoryName(Entry.ExePath);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            Process.Start("explorer.exe", $"\"{dir}\"");
        else
            _logger.LogWarning("OpenFolder: directory not found for {Name}.", Entry.Name);
    }

    private void Poll()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_process is null) return;
            if (_process.HasExited)
            {
                _timer.Stop();
                StatusText = "● Exited";
                IsRunning  = false;
                _process.Dispose();
                _process   = null;
            }
            else
            {
                StatusText = $"● Running (PID: {_process.Id})";
                IsRunning  = true;
            }
        });
    }

    /// <summary>Stops the polling timer and disposes the tracked process handle.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
        _process?.Dispose();
    }
}