// ============================================================
// DevPilot — IT Automation Hub v1.0
// HeartbeatViewModel.cs — ViewModel pentru Heartbeat Tab
// Phase: 8 (PATCH: AutoRestart toggle added)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using DevPilot.Core.Interfaces;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using DevPilot.UI.Commands;
using Microsoft.Extensions.Logging;

namespace DevPilot.UI.ViewModels;

public sealed class HeartbeatViewModel : BaseViewModel
{
    // ── Dependencies ──────────────────────────────────────────────────────
    private readonly HeartbeatService   _heartbeatService;  // concrete — need AutoRestart prop
    private readonly ISettingsManager   _settingsManager;
    private readonly ILogger<HeartbeatViewModel> _logger;

    // ── Observable collections ────────────────────────────────────────────
    public ObservableCollection<HeartbeatRowViewModel> Rows { get; } = new();

    // ── Status ────────────────────────────────────────────────────────────
    private string _statusText = "Monitor oprit.";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            SetProperty(ref _isRunning, value);
            OnPropertyChanged(nameof(StartStopLabel));
        }
    }

    public string StartStopLabel => IsRunning ? "⏹ Stop Monitor" : "▶ Start Monitor";

    // ── Interval ──────────────────────────────────────────────────────────
    private int _intervalSeconds = 30;
    public int IntervalSeconds
    {
        get => _intervalSeconds;
        set => SetProperty(ref _intervalSeconds, Math.Max(5, value));
    }

    // ── AutoRestart ───────────────────────────────────────────────────────
    private bool _autoRestart;
    /// <summary>
    /// When enabled, dead process targets are automatically relaunched.
    /// Equivalent to Master Orchestrator heartbeat.auto_restart = true.
    /// </summary>
    public bool AutoRestart
    {
        get => _autoRestart;
        set
        {
            SetProperty(ref _autoRestart, value);
            _heartbeatService.AutoRestart = value;
            _settingsManager.Current.HeartbeatAutoRestart = value;
            _settingsManager.Save();
            _logger.LogInformation("Heartbeat AutoRestart set to {Value}", value);
        }
    }

    // ── Add-target form ───────────────────────────────────────────────────
    private string _newName   = string.Empty;
    public string NewName
    {
        get => _newName;
        set => SetProperty(ref _newName, value);
    }

    private string _newTarget = string.Empty;
    public string NewTarget
    {
        get => _newTarget;
        set => SetProperty(ref _newTarget, value);
    }

    private HeartbeatTargetType _newType = HeartbeatTargetType.Process;
    public HeartbeatTargetType NewType
    {
        get => _newType;
        set => SetProperty(ref _newType, value);
    }

    public IEnumerable<HeartbeatTargetType> TargetTypes =>
        Enum.GetValues<HeartbeatTargetType>();

    // ── Commands ──────────────────────────────────────────────────────────
    public ICommand StartStopCommand    { get; }
    public ICommand AddTargetCommand    { get; }
    public ICommand RemoveTargetCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────
    public HeartbeatViewModel(
        HeartbeatService heartbeatService,          
        ISettingsManager settingsManager,
        ILogger<HeartbeatViewModel> logger)
    {
        _heartbeatService = heartbeatService;        
        _settingsManager = settingsManager;
        _logger = logger;

        _autoRestart = settingsManager.Current.HeartbeatAutoRestart;
        heartbeatService.AutoRestart = _autoRestart; 

        heartbeatService.StatusChanged += OnStatusChanged;

        StartStopCommand    = new RelayCommand(_ => ToggleMonitor());
        AddTargetCommand    = new RelayCommand(_ => AddTarget(),
                                              _ => !string.IsNullOrWhiteSpace(NewName)
                                                && !string.IsNullOrWhiteSpace(NewTarget));
        RemoveTargetCommand = new RelayCommand(p => RemoveTarget(p as HeartbeatRowViewModel),
                                              p => p is HeartbeatRowViewModel);

        LoadTargets();
    }

    // ── Load saved targets ────────────────────────────────────────────────
    private void LoadTargets()
    {
        Rows.Clear();
        foreach (var t in _settingsManager.Current.HeartbeatTargets)
            Rows.Add(new HeartbeatRowViewModel(t));
    }

    // ── Start / Stop ──────────────────────────────────────────────────────
    private void ToggleMonitor()
    {
        if (IsRunning)
        {
            _heartbeatService.Stop();
            IsRunning  = false;
            StatusText = "Monitor oprit.";
        }
        else
        {
            var targets = _settingsManager.Current.HeartbeatTargets;
            if (targets.Count == 0)
            {
                StatusText = "⚠️ Nu există targets configurate.";
                return;
            }
            _heartbeatService.Start(targets, IntervalSeconds);
            IsRunning  = true;
            StatusText = $"▶ Monitoring {targets.Count} target(s) — interval {IntervalSeconds}s"
                       + (AutoRestart ? " | 🔄 AutoRestart ON" : "");
        }
    }

    // ── Add target ────────────────────────────────────────────────────────
    private void AddTarget()
    {
        var target = new HeartbeatTarget
        {
            Name      = NewName.Trim(),
            Target    = NewTarget.Trim(),
            Type      = NewType,
            IsEnabled = true
        };

        _settingsManager.Current.HeartbeatTargets.Add(target);
        _settingsManager.Save();
        Rows.Add(new HeartbeatRowViewModel(target));

        NewName   = string.Empty;
        NewTarget = string.Empty;

        _logger.LogInformation("Heartbeat target added: {Name}", target.Name);
    }

    // ── Remove target ─────────────────────────────────────────────────────
    private void RemoveTarget(HeartbeatRowViewModel? row)
    {
        if (row is null) return;
        _settingsManager.Current.HeartbeatTargets
            .RemoveAll(t => t.Id == row.Id);
        _settingsManager.Save();
        Rows.Remove(row);
        _logger.LogInformation("Heartbeat target removed: {Name}", row.Name);
    }

    // ── Status changed callback ───────────────────────────────────────────
    private void OnStatusChanged(object? sender, HeartbeatStatusChangedEventArgs e)
    {
        var row = Rows.FirstOrDefault(r => r.Id == e.Target.Id);
        if (row is not null)
            row.Status = e.NewStatus;
    }
}
