// ============================================================
// DevPilot — IT Automation Hub v1.0
// HeartbeatRowViewModel.cs — Row item in Heartbeat grid
// Phase: 8
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using DevPilot.Models;

namespace DevPilot.UI.ViewModels;

/// <summary>Observable row for the Heartbeat DataGrid.</summary>
public sealed class HeartbeatRowViewModel : BaseViewModel
{
    public string Id   { get; }
    public string Name { get; }
    public string Target { get; }
    public string TypeLabel { get; }

    private HeartbeatStatus _status = HeartbeatStatus.Unknown;
    public HeartbeatStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusEmoji));
                OnPropertyChanged(nameof(StatusLabel));
            }
        }
    }

    public string StatusEmoji => Status switch
    {
        HeartbeatStatus.Alive   => "✅",
        HeartbeatStatus.Dead    => "❌",
        _                       => "⚠️"
    };

    public string StatusLabel => Status switch
    {
        HeartbeatStatus.Alive   => "Alive",
        HeartbeatStatus.Dead    => "Dead",
        _                       => "Unknown"
    };

    private string _lastChecked = "—";
    public string LastChecked
    {
        get => _lastChecked;
        set => SetProperty(ref _lastChecked, value);
    }

    public HeartbeatRowViewModel(HeartbeatTarget t)
    {
        Id        = t.Id;
        Name      = t.Name;
        Target    = t.Target;
        TypeLabel = t.Type == HeartbeatTargetType.Process ? "Process" : "URL";
    }
}
