using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

public sealed record ToolStatus(string ToolId, ToolRunState State, IReadOnlyList<int> ProcessIds, string Detail)
{
    public static ToolStatus Unknown(string id) => new(id, ToolRunState.Unknown, [], string.Empty);
    public bool IsRunning => State == ToolRunState.Running;
}

public sealed record HealthStatus(string TargetId, bool? Alive, string Detail);

public interface IProcessMonitor : IDisposable
{
    event EventHandler? StatusChanged;
    /// <summary>A tool with auto-restart stopped unexpectedly.</summary>
    event EventHandler<ToolEntry>? AutoRestartRequested;
    ToolStatus Get(string toolId);
    IReadOnlyList<int> FindProcesses(ToolEntry tool);
    HealthStatus GetHealth(string targetId);
    void NotifyLaunched(string toolId, int processId);
    void NotifyLaunchFailed(string toolId, string error);
    Task RefreshNowAsync();
    void Start();
}
