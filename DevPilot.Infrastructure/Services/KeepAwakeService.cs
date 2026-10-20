using DevPilot.Core.Interfaces;
using DevPilot.Core.KeepAwake;
using DevPilot.Models;
using Microsoft.Extensions.Logging;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// The single keep-awake owner. A dedicated thread holds the power request
/// (SetThreadExecutionState is per thread, so it is always set and cleared on the same thread)
/// and optionally sends a harmless input nudge when the user is idle.
/// </summary>
public sealed class KeepAwakeService : IKeepAwakeService
{
    private readonly ILogger<KeepAwakeService> _logger;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private volatile KeepAwakeSettings _settings = new();
    private volatile bool _stopping;
    private bool _held;
    private bool _heldDisplay;
    private DateTime _lastNudge = DateTime.MinValue;

    public bool IsActive { get; private set; }
    public string StatusText { get; private set; } = "Off";
    public event EventHandler? StateChanged;

    public KeepAwakeService(ILogger<KeepAwakeService> logger)
    {
        _logger = logger;
        _thread = new Thread(Loop) { IsBackground = true, Name = "DevPilot keep-awake" };
        _thread.Start();
    }

    public void Apply(KeepAwakeSettings settings)
    {
        _settings = Copy(settings);
        _wake.Set();
    }

    private static KeepAwakeSettings Copy(KeepAwakeSettings s) => new()
    {
        Enabled = s.Enabled, Mode = s.Mode, KeepDisplayOn = s.KeepDisplayOn, IntervalSeconds = s.IntervalSeconds,
        OnlyWhenIdleSeconds = s.OnlyWhenIdleSeconds, ScheduleEnabled = s.ScheduleEnabled, ScheduleFrom = s.ScheduleFrom,
        ScheduleTo = s.ScheduleTo, ScheduleDays = [.. s.ScheduleDays],
    };

    private void Loop()
    {
        while (!_stopping)
        {
            try { Tick(); }
            catch (Exception ex) { _logger.LogError(ex, "Keep-awake tick failed"); }
            _wake.WaitOne(TimeSpan.FromSeconds(5));
        }
        Release();
    }

    private void Tick()
    {
        var s = _settings;
        bool active = KeepAwakeSchedule.IsActive(s, DateTime.Now);

        if (active && (!_held || _heldDisplay != s.KeepDisplayOn))
        {
            uint flags = NativeInput.ES_CONTINUOUS | NativeInput.ES_SYSTEM_REQUIRED | (s.KeepDisplayOn ? NativeInput.ES_DISPLAY_REQUIRED : 0);
            NativeInput.SetThreadExecutionState(flags);
            _held = true;
            _heldDisplay = s.KeepDisplayOn;
        }
        else if (!active && _held)
        {
            Release();
        }

        if (active && s.Mode != KeepAwakeMode.Silent)
        {
            var idle = NativeInput.IdleTime();
            if (idle >= TimeSpan.FromSeconds(s.OnlyWhenIdleSeconds)
                && DateTime.UtcNow - _lastNudge >= TimeSpan.FromSeconds(s.IntervalSeconds))
            {
                if (s.Mode == KeepAwakeMode.MouseNudge) NativeInput.NudgeMouse();
                else NativeInput.TapF15();
                _lastNudge = DateTime.UtcNow;
            }
        }

        string text = !s.Enabled ? "Off"
            : !active ? $"Waiting for schedule ({s.ScheduleFrom}–{s.ScheduleTo})"
            : s.Mode switch
            {
                KeepAwakeMode.Silent => "Active — silent (power request only)",
                KeepAwakeMode.MouseNudge => $"Active — mouse nudge every {s.IntervalSeconds} s when idle",
                _ => $"Active — F15 key every {s.IntervalSeconds} s when idle",
            };
        if (active != IsActive || text != StatusText)
        {
            IsActive = active;
            StatusText = text;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Release()
    {
        if (!_held) return;
        NativeInput.SetThreadExecutionState(NativeInput.ES_CONTINUOUS);
        _held = false;
    }

    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        _thread.Join(TimeSpan.FromSeconds(3));
        _wake.Dispose();
    }
}
