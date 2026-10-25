using DevPilot.Core.Launch;
using DevPilot.Models;

namespace DevPilot.Core.Cloud;

public enum GateOutcome
{
    /// <summary>Start the tool (delay-only mode, cloud ready, or "start anyway" after the timeout).</summary>
    Proceed,
    /// <summary>Cloud did not connect in time and the tool is set to be skipped.</summary>
    TimedOutSkip,
}

/// <summary>
/// Decides, per tool, whether autostart may go ahead. Tools on the same drive share the work: once a drive is
/// ready it is not checked again, and after a timeout a later tool does not wait the same time again.
/// </summary>
public sealed class CloudGate
{
    private readonly ICloudProbe _probe;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly IReadOnlyList<string> _oneDriveRoots;
    private readonly Dictionary<string, TimeSpan?> _known = new(StringComparer.OrdinalIgnoreCase); // null = ready, else time already waited in vain

    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public CloudGate(ICloudProbe probe, IEnumerable<string>? oneDriveRoots = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _probe = probe;
        _oneDriveRoots = (oneDriveRoots ?? []).ToList();
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Folder whose availability matters for the tool.</summary>
    public static string TargetFolder(ToolEntry tool)
    {
        return PathTokens.ToolDirectory(tool);
    }

    /// <param name="status">Receives a short text while waiting, null when the wait is over.</param>
    public async Task<GateOutcome> WaitAsync(ToolEntry tool, Action<string?>? status = null, CancellationToken ct = default)
    {
        var options = tool.AutoStart;
        if (options.Mode != AutoStartMode.WaitForCloud) return GateOutcome.Proceed;

        var folder = TargetFolder(tool);
        if (string.IsNullOrWhiteSpace(folder)) return GateOutcome.Proceed; // commands and links have no folder to wait for
        var provider = CloudReadiness.Resolve(options.CloudProvider, folder, _oneDriveRoots);
        if (provider == CloudProvider.Auto) return GateOutcome.Proceed; // not on a cloud drive: nothing to wait for

        var key = provider + "|" + CloudReadiness.RootOf(folder);
        var timeout = TimeSpan.FromSeconds(Math.Max(1, options.CloudTimeoutSeconds));
        var waited = TimeSpan.Zero;

        if (_known.TryGetValue(key, out var known))
        {
            if (known is null) return GateOutcome.Proceed;
            if (known >= timeout) return Timeout(options);
            waited = known.Value;
        }

        var name = CloudReadiness.DisplayName(provider);
        while (true)
        {
            if (CloudReadiness.IsReady(provider, folder, _probe))
            {
                _known[key] = null;
                status?.Invoke(null);
                return GateOutcome.Proceed;
            }
            if (waited >= timeout)
            {
                _known[key] = waited;
                status?.Invoke(null);
                return Timeout(options);
            }
            status?.Invoke($"Waiting for {name} ({(int)waited.TotalSeconds}/{(int)timeout.TotalSeconds} s)");
            var step = TimeSpan.FromTicks(Math.Min(PollInterval.Ticks, (timeout - waited).Ticks));
            await _delay(step, ct).ConfigureAwait(false);
            waited += step;
        }
    }

    private static GateOutcome Timeout(AutoStartOptions options) =>
        options.OnTimeout == CloudTimeoutAction.StartAnyway ? GateOutcome.Proceed : GateOutcome.TimedOutSkip;
}
