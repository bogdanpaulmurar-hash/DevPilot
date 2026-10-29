using System.ComponentModel;
using System.Diagnostics;
using DevPilot.Core.Guard;
using DevPilot.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DevPilot.Infrastructure.Services;

/// <summary>Pause / resume external access for all managed tools. The state is saved in the config.</summary>
public sealed class KillSwitchService
{
    private readonly IConfigStore _config;
    private readonly IProcessMonitor _processes;
    private readonly ILogger<KillSwitchService> _logger;

    public KillSwitchService(IConfigStore config, IProcessMonitor processes, ILogger<KillSwitchService> logger)
    {
        _config = config;
        _processes = processes;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public bool IsPaused => _config.Current.Guard.Paused;

    /// <summary>Pauses and stops the running network-capable tools. Returns how many tools were stopped.</summary>
    public async Task<int> PauseAsync()
    {
        _config.Current.Guard.Paused = true;
        _config.Touch(ConfigArea.Monitor);

        var running = _config.Current.Tools.ToList()
            .Where(t => _processes.Get(t.Id).IsRunning && KillSwitch.IsNetworkCapable(t, GuardManifest.Network(t)))
            .ToList();
        int stopped = 0;
        await Task.Run(() =>
        {
            foreach (var tool in running)
            {
                bool any = false;
                foreach (var pid in _processes.Get(tool.Id).ProcessIds)
                {
                    try
                    {
                        using var p = Process.GetProcessById(pid);
                        p.Kill(entireProcessTree: true);
                        any = true;
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
                    {
                        _logger.LogDebug(ex, "Stopping pid {Pid} failed", pid);
                    }
                }
                if (any) stopped++;
            }
        }).ConfigureAwait(false);

        _logger.LogWarning("Egress Guard: external access paused, {Count} tool(s) stopped", stopped);
        await _processes.RefreshNowAsync().ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return stopped;
    }

    public void Resume()
    {
        _config.Current.Guard.Paused = false;
        _config.Touch(ConfigArea.Monitor);
        _logger.LogInformation("Egress Guard: external access resumed");
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
