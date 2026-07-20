// ============================================================
// DevPilot — IT Automation Hub v1.0
// IHeartbeatService.cs — Contract pentru Heartbeat Monitor
// Phase: 8
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

/// <summary>Monitors processes and URLs at configurable intervals.</summary>
public interface IHeartbeatService : IDisposable
{
    /// <summary>Raised on the UI thread when a target's status changes.</summary>
    event EventHandler<HeartbeatStatusChangedEventArgs>? StatusChanged;

    /// <summary>True while monitoring is active.</summary>
    bool IsRunning { get; }

    /// <summary>Start monitoring the given targets.</summary>
    void Start(IEnumerable<HeartbeatTarget> targets, int intervalSeconds = 30);

    /// <summary>Stop all monitoring.</summary>
    void Stop();
}

/// <summary>Event args for a heartbeat status change.</summary>
public sealed class HeartbeatStatusChangedEventArgs : EventArgs
{
    public HeartbeatTarget Target  { get; }
    public HeartbeatStatus OldStatus { get; }
    public HeartbeatStatus NewStatus { get; }

    public HeartbeatStatusChangedEventArgs(
        HeartbeatTarget target,
        HeartbeatStatus oldStatus,
        HeartbeatStatus newStatus)
    {
        Target    = target;
        OldStatus = oldStatus;
        NewStatus = newStatus;
    }
}
