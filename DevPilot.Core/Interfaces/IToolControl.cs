using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

/// <summary>Is a managed tool running, stop it, start it: what an update needs, apart from the launcher details.</summary>
public interface IToolControl
{
    bool IsRunning(ToolEntry tool);
    /// <summary>Stops the tool and waits (a few seconds) until its processes are gone.</summary>
    Task StopAsync(ToolEntry tool);
    Task<LaunchResult> StartAsync(ToolEntry tool);
}
