// ============================================================
// DevPilot — IT Automation Hub
// JigglerViewModel.cs — ViewModel for Mouse Jiggler tab
// Phase: 4 (PATCH: IntervalLabel + IsNotRunning added)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Windows.Input;
using DevPilot.Core.Interfaces;
using DevPilot.UI.Commands;
using Microsoft.Extensions.Logging;

namespace DevPilot.UI.ViewModels;

/// <summary>
/// ViewModel for the Mouse Jiggler tab. Binds to JigglerView.xaml.
/// </summary>
public class JigglerViewModel : BaseViewModel, IDisposable
{
    private readonly IJigglerService  _jiggler;
    private readonly ISettingsManager _settings;
    private readonly ILogger<JigglerViewModel> _logger;
    private bool _disposed;

    // ── Properties ───────────────────────────────────────────────────────

    private bool _isRunning;
    /// <summary>True when jiggler is active.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ToggleLabel));
            OnPropertyChanged(nameof(IsNotRunning));   // ← PATCH
        }
    }

    /// <summary>Inverse of IsRunning — used to enable/disable the interval slider.</summary>
    public bool IsNotRunning => !IsRunning;                                    // ← PATCH

    private int _intervalSeconds;
    /// <summary>Interval in seconds between jiggle moves (5–600).</summary>
    public int IntervalSeconds
    {
        get => _intervalSeconds;
        set
        {
            if (value < 5)   value = 5;
            if (value > 600) value = 600;
            _intervalSeconds = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IntervalDisplay));
            OnPropertyChanged(nameof(IntervalLabel));  // ← PATCH
            _settings.Current.JigglerIntervalSec = value;
            _settings.Save();
        }
    }

    public string IntervalDisplay => $"{_intervalSeconds}s";

    /// <summary>Human-readable label for the slider — e.g. "Interval: 60s".</summary>
    public string IntervalLabel => $"Interval: {_intervalSeconds}s";           // ← PATCH

    public string StatusText  => IsRunning
        ? "🟢  Active — preventing session timeout"
        : "⚪  Inactive";

    public string ToggleLabel => IsRunning ? "Stop Jiggler" : "Start Jiggler";

    // ── Commands ─────────────────────────────────────────────────────────
    public ICommand ToggleCommand { get; }

    // ── Constructor ──────────────────────────────────────────────────────
    public JigglerViewModel(
        IJigglerService  jiggler,
        ISettingsManager settings,
        ILogger<JigglerViewModel> logger)
    {
        _jiggler  = jiggler;
        _settings = settings;
        _logger   = logger;

        _intervalSeconds = _settings.Current.JigglerIntervalSec > 0
            ? _settings.Current.JigglerIntervalSec
            : 60;

        // Auto-start if was enabled last session
        if (settings.Current.JigglerEnabled)
            StartJiggler();

        ToggleCommand = new RelayCommand(_ => Toggle());
    }

    // ── Handlers ─────────────────────────────────────────────────────────

    private void Toggle()
    {
        if (IsRunning) StopJiggler();
        else           StartJiggler();
    }

    private void StartJiggler()
    {
        _jiggler.Start(_intervalSeconds);
        IsRunning = true;
        _settings.Current.JigglerEnabled = true;
        _settings.Save();
        _logger.LogInformation("Jiggler started by user (interval={Sec}s)", _intervalSeconds);
    }

    private void StopJiggler()
    {
        _jiggler.Stop();
        IsRunning = false;
        _settings.Current.JigglerEnabled = false;
        _settings.Save();
        _logger.LogInformation("Jiggler stopped by user");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _jiggler.Dispose();
        _disposed = true;
    }
}
