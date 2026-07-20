// ============================================================
// DevPilot — IT Automation Hub
// JigglerService.cs — Mouse Jiggler background service
// Phase: 4 (FIX: added SetThreadExecutionState to prevent sleep)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Runtime.InteropServices;
using DevPilot.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// Moves the mouse cursor by ±1 pixel at a configurable interval
/// AND calls SetThreadExecutionState to prevent Windows sleep/lock
/// even under strict Group Policy power plans (Siemens).
/// </summary>
public sealed class JigglerService : IJigglerService
{
    // ── Win32 — Cursor ───────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    // ── Win32 — Power / Sleep prevention ────────────────────────────────
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    // Keeps display ON + prevents system sleep
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    // ── Fields ───────────────────────────────────────────────────────────
    private readonly ILogger<JigglerService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _task;
    private bool _disposed;

    public bool IsRunning => _task is { IsCompleted: false };

    public JigglerService(ILogger<JigglerService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Starts the jiggler loop.
    /// - Moves cursor ±1 px every <paramref name="intervalSeconds"/> seconds.
    /// - Calls SetThreadExecutionState every tick to prevent system sleep.
    /// If already running, stops first and restarts with new interval.
    /// </summary>
    public void Start(int intervalSeconds)
    {
        Stop();

        if (intervalSeconds < 1) intervalSeconds = 1;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _task = Task.Run(async () =>
        {
            _logger.LogInformation("JigglerService started (interval={Sec}s)", intervalSeconds);
            bool toggle = false;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), token);

                    // ── 1. Prevent system sleep + keep display on ──────────
                    SetThreadExecutionState(
                        ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);

                    // ── 2. Move cursor ±1 px and restore ──────────────────
                    if (GetCursorPos(out var pt))
                    {
                        int offset = toggle ? 1 : -1;
                        SetCursorPos(pt.X + offset, pt.Y);
                        SetCursorPos(pt.X, pt.Y); // restore immediately
                        toggle = !toggle;
                        _logger.LogDebug("Jiggle at ({X},{Y}) offset={Off}", pt.X, pt.Y, offset);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "JigglerService loop error");
                }
            }

            // ── Release sleep prevention when stopped ──────────────────
            SetThreadExecutionState(ES_CONTINUOUS);
            _logger.LogInformation("JigglerService stopped");

        }, CancellationToken.None);
    }

    /// <summary>Stops the jiggler loop.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        // Release sleep prevention immediately on Stop()
        SetThreadExecutionState(ES_CONTINUOUS);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}