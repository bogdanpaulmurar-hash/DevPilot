using DevPilot.Core.Interfaces;
using DevPilot.Core.Launch;
using DevPilot.Models;

namespace DevPilot.Infrastructure.Services;

/// <summary>Stops, starts and checks managed tools for repository updates, through the launcher and the process monitor.</summary>
public sealed class ToolControl(ILauncher launcher, IProcessMonitor monitor) : IToolControl
{
    public bool IsRunning(ToolEntry tool) => monitor.FindProcesses(tool).Count > 0;

    public async Task StopAsync(ToolEntry tool)
    {
        await launcher.StopAsync(tool).ConfigureAwait(false);
        // Files stay locked until the processes are really gone.
        for (int i = 0; i < 20 && monitor.FindProcesses(tool).Count > 0; i++)
            await Task.Delay(250).ConfigureAwait(false);
    }

    public Task<LaunchResult> StartAsync(ToolEntry tool) => launcher.StartAsync(tool, LaunchReason.Restart);
}
