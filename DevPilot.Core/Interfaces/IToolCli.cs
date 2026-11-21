namespace DevPilot.Core.Interfaces;

/// <summary>Outcome of one hidden command-line call to a tool.</summary>
public sealed record ToolCliResult(int ExitCode, string Output, bool TimedOut = false, string? StartError = null)
{
    public bool Ok => StartError is null && !TimedOut && ExitCode == 0;
}

/// <summary>Runs a tool's own program without a window and returns what it printed (e.g. <c>ZonePilot layouts</c>).</summary>
public interface IToolCli
{
    Task<ToolCliResult> RunAsync(string program, string arguments, TimeSpan timeout, CancellationToken ct = default);
}
