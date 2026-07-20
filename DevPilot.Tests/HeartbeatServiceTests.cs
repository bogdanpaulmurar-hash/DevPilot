// ============================================================
// DevPilot — IT Automation Hub
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevPilot.Tests;

/// <summary>
/// Unit tests for <see cref="HeartbeatService"/> lifecycle.
/// All tests use an empty target list to avoid real network/process checks.
/// <see cref="System.Windows.Application.Current"/> is null in the test runner;
/// <see cref="HeartbeatService"/> handles this gracefully via its null-check guard.
/// </summary>
public sealed class HeartbeatServiceTests : IDisposable
{
    private readonly HeartbeatService _sut = new(NullLogger<HeartbeatService>.Instance);

    public void Dispose() => _sut.Dispose();

    private static HeartbeatTarget[] NoTargets => Array.Empty<HeartbeatTarget>();

    // ── Initial state ─────────────────────────────────────────────────────

    [Fact]
    public void IsRunning_WhenNew_ReturnsFalse()
    {
        Assert.False(_sut.IsRunning);
    }

    [Fact]
    public void AutoRestart_DefaultValue_IsFalse()
    {
        Assert.False(_sut.AutoRestart);
    }

    // ── Start ─────────────────────────────────────────────────────────────

    [Fact]
    public void Start_WithNoTargets_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.Start(NoTargets, 30));
        Assert.Null(ex);
    }

    [Fact]
    public void IsRunning_AfterStart_ReturnsTrue()
    {
        _sut.Start(NoTargets, 30);
        Assert.True(_sut.IsRunning);
    }

    [Fact]
    public void Start_ThenStartAgain_DoesNotThrow()
    {
        _sut.Start(NoTargets, 30);
        var ex = Record.Exception(() => _sut.Start(NoTargets, 10));
        Assert.Null(ex);
    }

    [Fact]
    public void Start_WithAllDisabledTargets_StartsAndFiltersOut()
    {
        var targets = new[]
        {
            new HeartbeatTarget
            {
                Id        = "t1",
                Name      = "Disabled",
                Target    = "notepad",
                Type      = HeartbeatTargetType.Process,
                IsEnabled = false  // should be ignored
            }
        };

        var ex = Record.Exception(() => _sut.Start(targets, 30));
        Assert.Null(ex);
        Assert.True(_sut.IsRunning);
    }

    // ── Stop ──────────────────────────────────────────────────────────────

    [Fact]
    public void Stop_WhenNotStarted_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.Stop());
        Assert.Null(ex);
    }

    [Fact]
    public void IsRunning_AfterStop_ReturnsFalse()
    {
        _sut.Start(NoTargets, 30);
        Assert.True(_sut.IsRunning);

        _sut.Stop(); // Stop() calls _workerTask.Wait(3s) — blocks until worker exits
        Assert.False(_sut.IsRunning);
    }

    // ── StatusChanged event ───────────────────────────────────────────────

    [Fact]
    public async Task StatusChanged_NotRaised_WhenNoTargetsConfigured()
    {
        bool eventRaised = false;
        _sut.StatusChanged += (_, _) => eventRaised = true;

        _sut.Start(NoTargets, intervalSeconds: 60); // long interval → no checks within test window
        await Task.Delay(300);                       // let worker settle into first delay
        _sut.Stop();

        Assert.False(eventRaised);
    }

    // ── Dispose ───────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_WhenRunning_DoesNotThrow()
    {
        _sut.Start(NoTargets, 30);
        var ex = Record.Exception(() => _sut.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        _sut.Dispose();
        var ex = Record.Exception(() => _sut.Dispose());
        Assert.Null(ex);
    }
}