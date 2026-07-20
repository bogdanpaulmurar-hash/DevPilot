// ============================================================
// DevPilot — IT Automation Hub
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using DevPilot.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevPilot.Tests;

/// <summary>Unit tests for <see cref="JigglerService"/> lifecycle and state management.</summary>
public sealed class JigglerServiceTests : IDisposable
{
    // xUnit creates a new instance per test — each test gets a fresh service.
    private readonly JigglerService _sut = new(NullLogger<JigglerService>.Instance);

    public void Dispose() => _sut.Dispose();

    // ── Initial state ─────────────────────────────────────────────────────

    [Fact]
    public void IsRunning_WhenNew_ReturnsFalse()
    {
        Assert.False(_sut.IsRunning);
    }

    // ── Start ─────────────────────────────────────────────────────────────

    [Fact]
    public void Start_WithValidInterval_SetsIsRunningTrue()
    {
        _sut.Start(60);
        Assert.True(_sut.IsRunning);
    }

    [Fact]
    public void Start_WithZeroInterval_NormalisesAndStarts()
    {
        var ex = Record.Exception(() => _sut.Start(0));
        Assert.Null(ex);
        Assert.True(_sut.IsRunning);
    }

    [Fact]
    public void Start_WithNegativeInterval_NormalisesAndStarts()
    {
        var ex = Record.Exception(() => _sut.Start(-99));
        Assert.Null(ex);
        Assert.True(_sut.IsRunning);
    }

    [Fact]
    public void Start_CalledTwice_DoesNotThrowAndRemainsRunning()
    {
        _sut.Start(60);
        var ex = Record.Exception(() => _sut.Start(30)); // second call stops + restarts
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
    public async Task Stop_WhenRunning_SetsIsRunningFalseEventually()
    {
        _sut.Start(60);
        Assert.True(_sut.IsRunning);

        _sut.Stop();
        await Task.Delay(500); // let the background loop exit on cancellation

        Assert.False(_sut.IsRunning);
    }

    // ── Dispose ───────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_WhenRunning_DoesNotThrow()
    {
        _sut.Start(60);
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