using DevPilot.Core.Launch;
using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

public sealed record LaunchResult(bool Success, int ProcessId, string? Error)
{
    public static LaunchResult Ok(int pid = 0) => new(true, pid, null);
    public static LaunchResult Fail(string error) => new(false, 0, error);
}

public interface ILauncher
{
    /// <summary>Raised after a tool's main process was started.</summary>
    event EventHandler<ToolEntry>? ToolStarted;
    Task<LaunchResult> StartAsync(ToolEntry tool, LaunchReason reason = LaunchReason.Manual);
    Task<LaunchResult> RunActionAsync(ToolEntry tool, ToolAction action);
    Task<LaunchResult> OpenPathAsync(string path);
    Task<LaunchResult> OpenLocationAsync(ToolEntry tool);
    Task<int> StopAsync(ToolEntry tool);
    Task<LaunchResult> RestartAsync(ToolEntry tool);
}
