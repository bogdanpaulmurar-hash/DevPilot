// ============================================================
// DevPilot — IT Automation Hub
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using DevPilot.Core.Interfaces;
using DevPilot.Models;
using DevPilot.UI.Commands;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace DevPilot.UI.ViewModels;

/// <summary>
/// ViewModel for the Tools tab. Manages launch and 2-second process monitoring
/// for three external tools. Implements <see cref="IDisposable"/> to release timers.
/// Paths are always read live from <see cref="ISettingsManager.Current"/> at launch time.
/// </summary>
public sealed class ExternalToolsViewModel : BaseViewModel, IDisposable
{
    private readonly ISettingsManager                _settings;
    private readonly ILogger<ExternalToolsViewModel> _logger;
    private bool _disposed;

    // ═══════════════════════════════════════════════════════════════════════
    // CUSTOM TOOLS — dynamic user-defined tool cards
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Dynamic collection of user-defined tools loaded from config.</summary>
    public ObservableCollection<CustomToolViewModel> CustomTools { get; } = [];

    /// <summary>Opens the Add Tool dialog and appends the result to the collection.</summary>
    public ICommand AddCustomToolCommand { get; }

    /// <summary>
    /// Removes a custom tool from the collection and settings.
    /// CommandParameter must be the <see cref="CustomToolViewModel"/> to remove.
    /// </summary>
    public ICommand RemoveCustomToolCommand { get; }

    /// <summary>Opens the Edit Tool dialog for an existing entry.</summary>
    public ICommand EditCustomToolCommand { get; }

    /// <summary>
    /// Delegate wired by MainWindow to show the edit dialog pre-filled.
    /// Receives the existing entry; returns new values or null on cancel.
    /// </summary>
    public Func<CustomToolEntry, (string Name, string Description, string Icon, string ExePath)?>?
        ShowEditToolDialog { get; set; }

    /// <summary>
    /// Delegate wired by <see cref="Views.MainWindow"/> to show the Add Tool dialog.
    /// Returns null when the user cancels.
    /// </summary>
    public Func<(string Name, string Description, string Icon, string ExePath)?>? ShowAddToolDialog
    { get; set; }

    // ═══════════════════════════════════════════════════════════════════════
    // TOOL 1 — DataCleansing Solution
    // ═══════════════════════════════════════════════════════════════════════

    private Process? _dcProcess;
    private readonly Timer _dcTimer = new(2000) { AutoReset = true };

    private string _dcStatusText = "● Not running";
    /// <summary>Live process status label for DataCleansing.</summary>
    public string DcStatusText
    {
        get => _dcStatusText;
        private set => SetProperty(ref _dcStatusText, value);
    }

    private bool _dcIsRunning;
    /// <summary>True while DataCleansing process is alive — used by DataTrigger to colour status green.</summary>
    public bool DcIsRunning
    {
        get => _dcIsRunning;
        private set => SetProperty(ref _dcIsRunning, value);
    }

    private string _dcWarning = string.Empty;
    /// <summary>Inline warning shown when exe path is not found.</summary>
    public string DcWarning
    {
        get => _dcWarning;
        private set { if (SetProperty(ref _dcWarning, value)) OnPropertyChanged(nameof(DcWarningVisibility)); }
    }

    /// <summary>Collapses the warning banner when <see cref="DcWarning"/> is empty.</summary>
    public Visibility DcWarningVisibility =>
        string.IsNullOrEmpty(_dcWarning) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Launch DataCleansing.MasterApp.Starter.exe.</summary>
    public ICommand LaunchDataCleansingCommand { get; }

    /// <summary>Open the DataCleansing exe directory in Explorer.</summary>
    public ICommand OpenFolderDataCleansingCommand { get; }

    // ═══════════════════════════════════════════════════════════════════════
    // TOOL 2 — TestClientUtilityApp
    // ═══════════════════════════════════════════════════════════════════════

    private Process? _tcProcess;
    private readonly Timer _tcTimer = new(2000) { AutoReset = true };

    private string _tcStatusText = "● Not running";
    /// <summary>Live process status label for TestClientUtilityApp.</summary>
    public string TcStatusText
    {
        get => _tcStatusText;
        private set => SetProperty(ref _tcStatusText, value);
    }

    private bool _tcIsRunning;
    /// <summary>True while TestClientUtilityApp process is alive.</summary>
    public bool TcIsRunning
    {
        get => _tcIsRunning;
        private set => SetProperty(ref _tcIsRunning, value);
    }

    private string _tcWarning = string.Empty;
    /// <summary>Inline warning shown when TestClient exe path is not found.</summary>
    public string TcWarning
    {
        get => _tcWarning;
        private set { if (SetProperty(ref _tcWarning, value)) OnPropertyChanged(nameof(TcWarningVisibility)); }
    }

    /// <summary>Collapses the TestClient warning banner when warning is empty.</summary>
    public Visibility TcWarningVisibility =>
        string.IsNullOrEmpty(_tcWarning) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Launch TestClientUtilityApp.exe.</summary>
    public ICommand LaunchTestClientCommand { get; }

    /// <summary>Open the TestClientUtilityApp exe directory in Explorer.</summary>
    public ICommand OpenFolderTestClientCommand { get; }

    // ═══════════════════════════════════════════════════════════════════════
    // TOOL 3 — TC Management Local Starter
    // ═══════════════════════════════════════════════════════════════════════

    private Process? _mgmtProcess;
    private readonly Timer _mgmtTimer = new(2000) { AutoReset = true };

    private string _mgmtStatusText = "● Not running";
    /// <summary>Live process status label for TC Mgmt Starter.</summary>
    public string MgmtStatusText
    {
        get => _mgmtStatusText;
        private set => SetProperty(ref _mgmtStatusText, value);
    }

    private bool _mgmtIsRunning;
    /// <summary>True while TCMgmtLocalAppStarter process is alive.</summary>
    public bool MgmtIsRunning
    {
        get => _mgmtIsRunning;
        private set => SetProperty(ref _mgmtIsRunning, value);
    }

    private string _mgmtWarning = string.Empty;
    /// <summary>Inline warning shown when TC Mgmt Starter exe path is not found.</summary>
    public string MgmtWarning
    {
        get => _mgmtWarning;
        private set { if (SetProperty(ref _mgmtWarning, value)) OnPropertyChanged(nameof(MgmtWarningVisibility)); }
    }

    /// <summary>Collapses the TC Mgmt Starter warning banner when warning is empty.</summary>
    public Visibility MgmtWarningVisibility =>
        string.IsNullOrEmpty(_mgmtWarning) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Launch TCMgmtLocalAppStarter.exe.</summary>
    public ICommand LaunchTcMgmtStarterCommand { get; }

    /// <summary>Open the TC Mgmt Starter exe directory in Explorer.</summary>
    public ICommand OpenFolderTcMgmtStarterCommand { get; }

    // ═══════════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Initialises the ViewModel, wires commands and polling timers.
    /// ILogger&lt;ExternalToolsViewModel&gt; is provided automatically by the DI container.
    /// </summary>
    public ExternalToolsViewModel(
        ISettingsManager                settings,
        ILogger<ExternalToolsViewModel> logger)
    {
        _settings = settings;
        _logger   = logger;

        // ── Commands ──────────────────────────────────────────────────────
        LaunchDataCleansingCommand     = new RelayCommand(_ => LaunchDc());
        OpenFolderDataCleansingCommand = new RelayCommand(_ => OpenFolderDc());

        LaunchTestClientCommand        = new RelayCommand(_ => LaunchTc());
        OpenFolderTestClientCommand    = new RelayCommand(_ => OpenFolderTc());

        LaunchTcMgmtStarterCommand     = new RelayCommand(_ => LaunchMgmt());
        OpenFolderTcMgmtStarterCommand = new RelayCommand(_ => OpenFolderMgmt());

        // ── Polling timers ────────────────────────────────────────────────
        _dcTimer.Elapsed   += (_, _) => PollDc();
        _tcTimer.Elapsed   += (_, _) => PollTc();
        _mgmtTimer.Elapsed += (_, _) => PollMgmt();

        // ── Initial warning state ─────────────────────────────────────────
        RefreshAllWarnings();

        // ── Custom tools ──────────────────────────────────────────────────
        AddCustomToolCommand    = new RelayCommand(_ => AddCustomTool());
        RemoveCustomToolCommand = new RelayCommand(p => RemoveCustomTool((CustomToolViewModel)p!));
        EditCustomToolCommand   = new RelayCommand(p => EditCustomTool((CustomToolViewModel)p!));

        foreach (var entry in _settings.Current.CustomTools)
            CustomTools.Add(new CustomToolViewModel(entry, _logger));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PUBLIC — Refresh warnings after Settings → Save
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Re-evaluates File.Exists for all three tool paths.
    /// Call this after the user saves updated paths in Settings.
    /// </summary>
    public void RefreshAllWarnings()
    {
        DcWarning   = File.Exists(_settings.Current.DataCleansingExePath)
            ? string.Empty
            : "⚠  Path not configured or file not found. Go to Settings → Paths.";

        TcWarning   = File.Exists(_settings.Current.TestClientExePath)
            ? string.Empty
            : "⚠  Path not configured or file not found. Go to Settings → Paths.";

        MgmtWarning = File.Exists(_settings.Current.TcMgmtStarterExePath)
            ? string.Empty
            : "⚠  Path not configured or file not found. Go to Settings → Paths.";

        // Refresh custom tool warnings too
        foreach (var ct in CustomTools)
            ct.RefreshWarning();
    }
    // ═══════════════════════════════════════════════════════════════════════
    // PRIVATE — Launch handlers
    // ═══════════════════════════════════════════════════════════════════════

    private void LaunchDc()
    {
        var path = _settings.Current.DataCleansingExePath;
        if (!File.Exists(path))
        {
            DcWarning    = "⚠  Executable not found. Set the correct path in Settings → Paths.";
            DcStatusText = "⚠  Not configured.";
            _logger.LogWarning("DataCleansing exe not found: {Path}", path);
            return;
        }

        try
        {
            _dcProcess   = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            DcWarning    = string.Empty;
            DcStatusText = $"● Running (PID: {_dcProcess?.Id})";
            DcIsRunning  = _dcProcess is not null;
            if (_dcProcess is not null)
            {
                _dcTimer.Start();
                _logger.LogInformation("DataCleansing launched. PID={Pid}", _dcProcess.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DataCleansing launch failed.");
            DcStatusText = $"⚠  Launch failed: {ex.Message}";
            DcIsRunning  = false;
        }
    }

    private void LaunchTc()
    {
        var path = _settings.Current.TestClientExePath;
        if (!File.Exists(path))
        {
            TcWarning    = "⚠  Executable not found. Set the correct path in Settings → Paths.";
            TcStatusText = "⚠  Not configured.";
            _logger.LogWarning("TestClientUtilityApp exe not found: {Path}", path);
            return;
        }

        try
        {
            _tcProcess   = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            TcWarning    = string.Empty;
            TcStatusText = $"● Running (PID: {_tcProcess?.Id})";
            TcIsRunning  = _tcProcess is not null;
            if (_tcProcess is not null)
            {
                _tcTimer.Start();
                _logger.LogInformation("TestClientUtilityApp launched. PID={Pid}", _tcProcess.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TestClientUtilityApp launch failed.");
            TcStatusText = $"⚠  Launch failed: {ex.Message}";
            TcIsRunning  = false;
        }
    }

    private void LaunchMgmt()
    {
        var path = _settings.Current.TcMgmtStarterExePath;
        if (!File.Exists(path))
        {
            MgmtWarning    = "⚠  Executable not found. Set the correct path in Settings → Paths.";
            MgmtStatusText = "⚠  Not configured.";
            _logger.LogWarning("TcMgmtStarter exe not found: {Path}", path);
            return;
        }

        try
        {
            _mgmtProcess   = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            MgmtWarning    = string.Empty;
            MgmtStatusText = $"● Running (PID: {_mgmtProcess?.Id})";
            MgmtIsRunning  = _mgmtProcess is not null;
            if (_mgmtProcess is not null)
            {
                _mgmtTimer.Start();
                _logger.LogInformation("TcMgmtStarter launched. PID={Pid}", _mgmtProcess.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TcMgmtStarter launch failed.");
            MgmtStatusText = $"⚠  Launch failed: {ex.Message}";
            MgmtIsRunning  = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PRIVATE — Polling (System.Timers fires on ThreadPool — must Dispatch)
    // ═══════════════════════════════════════════════════════════════════════

    private void PollDc()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_dcProcess is null) return;
            if (_dcProcess.HasExited)
            {
                _dcTimer.Stop();
                DcStatusText = "● Exited";
                DcIsRunning  = false;
                _dcProcess.Dispose();
                _dcProcess   = null;
            }
            else
            {
                DcStatusText = $"● Running (PID: {_dcProcess.Id})";
                DcIsRunning  = true;
            }
        });
    }

    private void PollTc()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_tcProcess is null) return;
            if (_tcProcess.HasExited)
            {
                _tcTimer.Stop();
                TcStatusText = "● Exited";
                TcIsRunning  = false;
                _tcProcess.Dispose();
                _tcProcess   = null;
            }
            else
            {
                TcStatusText = $"● Running (PID: {_tcProcess.Id})";
                TcIsRunning  = true;
            }
        });
    }

    private void PollMgmt()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_mgmtProcess is null) return;
            if (_mgmtProcess.HasExited)
            {
                _mgmtTimer.Stop();
                MgmtStatusText = "● Exited";
                MgmtIsRunning  = false;
                _mgmtProcess.Dispose();
                _mgmtProcess   = null;
            }
            else
            {
                MgmtStatusText = $"● Running (PID: {_mgmtProcess.Id})";
                MgmtIsRunning  = true;
            }
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PRIVATE — Open Folder (always reads live path from settings)
    // ═══════════════════════════════════════════════════════════════════════

    private void OpenFolderDc()
    {
        var dir = Path.GetDirectoryName(_settings.Current.DataCleansingExePath);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            Process.Start("explorer.exe", $"\"{dir}\"");
        else
            _logger.LogWarning("OpenFolder: DataCleansing directory not found.");
    }

    private void OpenFolderTc()
    {
        var dir = Path.GetDirectoryName(_settings.Current.TestClientExePath);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            Process.Start("explorer.exe", $"\"{dir}\"");
        else
            _logger.LogWarning("OpenFolder: TestClient directory not found.");
    }

    private void OpenFolderMgmt()
    {
        var dir = Path.GetDirectoryName(_settings.Current.TcMgmtStarterExePath);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            Process.Start("explorer.exe", $"\"{dir}\"");
        else
            _logger.LogWarning("OpenFolder: TcMgmtStarter directory not found.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // IDisposable
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Stops all polling timers and disposes tracked process handles.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var ct in CustomTools)
            ct.Dispose();
        CustomTools.Clear();

        _dcTimer.Stop();   _dcTimer.Dispose();
        _tcTimer.Stop();   _tcTimer.Dispose();
        _mgmtTimer.Stop(); _mgmtTimer.Dispose();

        _dcProcess?.Dispose();
        _tcProcess?.Dispose();
        _mgmtProcess?.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CUSTOM TOOLS — Add / Remove
    // ═══════════════════════════════════════════════════════════════════════

    private void AddCustomTool()
    {
        var result = ShowAddToolDialog?.Invoke();
        if (result is null) return;

        var entry = new CustomToolEntry
        {
            Name        = result.Value.Name,
            Description = result.Value.Description,
            Icon        = result.Value.Icon,
            ExePath     = result.Value.ExePath,
        };

        _settings.Current.CustomTools.Add(entry);
        _settings.Save();

        CustomTools.Add(new CustomToolViewModel(entry, _logger));
        _logger.LogInformation("Custom tool added: {Name}", entry.Name);
    }

    private void RemoveCustomTool(CustomToolViewModel vm)
    {
        vm.Dispose();
        CustomTools.Remove(vm);
        _settings.Current.CustomTools.RemoveAll(e => e.Id == vm.Entry.Id);
        _settings.Save();
        _logger.LogInformation("Custom tool removed: {Name}", vm.Entry.Name);
    }

    private void EditCustomTool(CustomToolViewModel vm)
    {
        var result = ShowEditToolDialog?.Invoke(vm.Entry);
        if (result is null) return;

        vm.UpdateEntry(result.Value.Name, result.Value.Description,
                       result.Value.Icon, result.Value.ExePath);
        _settings.Save();
        _logger.LogInformation("Custom tool edited: {Name}", vm.Entry.Name);
    }
}
