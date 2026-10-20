using System.ComponentModel;
using System.Diagnostics;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Launch;
using DevPilot.Models;
using Microsoft.Extensions.Logging;

namespace DevPilot.Infrastructure.Services;

/// <summary>Starts tools and actions from a <see cref="LaunchPlan"/>; stops tools found by the monitor.</summary>
public sealed class Launcher : ILauncher
{
    private readonly ILogger<Launcher> _logger;
    private readonly IProcessMonitor _monitor;

    public event EventHandler<ToolEntry>? ToolStarted;

    public Launcher(ILogger<Launcher> logger, IProcessMonitor monitor)
    {
        _logger = logger;
        _monitor = monitor;
    }

    public async Task<LaunchResult> StartAsync(ToolEntry tool, LaunchReason reason = LaunchReason.Manual)
    {
        LaunchPlan plan;
        try { plan = LaunchPlanner.ForTool(tool, reason); }
        catch (Exception ex) { return LaunchResult.Fail(ex.Message); }

        var result = await Task.Run(() => Execute(plan)).ConfigureAwait(false);
        if (plan.IsMainProcess && tool.IsProcessLike)
        {
            if (result.Success) _monitor.NotifyLaunched(tool.Id, result.ProcessId);
            else _monitor.NotifyLaunchFailed(tool.Id, result.Error ?? "Start failed");
        }
        if (result.Success)
        {
            _logger.LogInformation("Started {Tool} ({Reason}): {Plan}", tool.Name, reason, plan);
            if (plan.IsMainProcess) ToolStarted?.Invoke(this, tool);
        }
        return result;
    }

    public Task<LaunchResult> RunActionAsync(ToolEntry tool, ToolAction action)
    {
        LaunchPlan plan;
        try { plan = LaunchPlanner.ForAction(tool, action); }
        catch (Exception ex) { return Task.FromResult(LaunchResult.Fail(ex.Message)); }
        _logger.LogInformation("Action {Tool} / {Action}: {Plan}", tool.Name, action.Label, plan);
        return Task.Run(() => Execute(plan));
    }

    public Task<LaunchResult> OpenPathAsync(string path)
    {
        var plan = Directory.Exists(path)
            ? new LaunchPlan { Mode = LaunchMode.ShellOpen, FileName = "explorer.exe", Arguments = CommandLineText.Quote(path) }
            : new LaunchPlan { Mode = LaunchMode.ShellOpen, FileName = path, WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty };
        return Task.Run(() => Execute(plan));
    }

    public Task<LaunchResult> OpenLocationAsync(ToolEntry tool) =>
        Task.Run(() => Execute(LaunchPlanner.ForLocation(tool)));

    public async Task<int> StopAsync(ToolEntry tool)
    {
        var pids = _monitor.FindProcesses(tool);
        int killed = 0;
        await Task.Run(() =>
        {
            foreach (var pid in pids)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                    killed++;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
                {
                    _logger.LogDebug(ex, "Stopping pid {Pid} failed", pid);
                }
            }
        }).ConfigureAwait(false);
        _logger.LogInformation("Stopped {Tool}: {Count} process(es)", tool.Name, killed);
        await _monitor.RefreshNowAsync().ConfigureAwait(false);
        return killed;
    }

    public async Task<LaunchResult> RestartAsync(ToolEntry tool)
    {
        await StopAsync(tool).ConfigureAwait(false);
        for (int i = 0; i < 20 && _monitor.FindProcesses(tool).Count > 0; i++)
            await Task.Delay(250).ConfigureAwait(false);
        return await StartAsync(tool, LaunchReason.Restart).ConfigureAwait(false);
    }

    internal LaunchResult Execute(LaunchPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.FileName)) return LaunchResult.Fail("Nothing to start: the path is empty.");
        try
        {
            return plan.Mode == LaunchMode.ShellOpen ? ShellOpen(plan) : StartProcess(plan, plan.FileName);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2 && TryPythonLauncher(plan.FileName, out var alt))
        {
            try { return StartProcess(plan, alt); }
            catch (Exception ex2) { return Fail(plan, ex2); }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return LaunchResult.Fail("Cancelled (administrator prompt declined).");
        }
        catch (Exception ex)
        {
            return Fail(plan, ex);
        }
    }

    private LaunchResult Fail(LaunchPlan plan, Exception ex)
    {
        _logger.LogError(ex, "Launch failed: {Plan}", plan);
        var msg = ex is Win32Exception { NativeErrorCode: 2 }
            ? $"Not found: {plan.FileName}"
            : ex.Message;
        return LaunchResult.Fail(msg);
    }

    /// <summary>pythonw.exe missing from PATH → try the py launcher (pyw.exe / py.exe).</summary>
    private static bool TryPythonLauncher(string fileName, out string alternative)
    {
        alternative = Path.GetFileName(fileName).ToLowerInvariant() switch
        {
            "pythonw.exe" => "pyw.exe",
            "python.exe" => "py.exe",
            _ => string.Empty,
        };
        return alternative.Length > 0;
    }

    private static LaunchResult StartProcess(LaunchPlan plan, string fileName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = plan.Arguments,
            UseShellExecute = false,
            CreateNoWindow = plan.HideWindow,
        };
        if (!string.IsNullOrWhiteSpace(plan.WorkingDirectory) && Directory.Exists(plan.WorkingDirectory))
            psi.WorkingDirectory = plan.WorkingDirectory;
        foreach (var (k, v) in plan.Environment) psi.Environment[k] = v;
        using var p = Process.Start(psi);
        return LaunchResult.Ok(p?.Id ?? 0);
    }

    private static LaunchResult ShellOpen(LaunchPlan plan)
    {
        var psi = new ProcessStartInfo
        {
            FileName = plan.FileName,
            Arguments = plan.Arguments,
            UseShellExecute = true,
        };
        if (!string.IsNullOrWhiteSpace(plan.WorkingDirectory) && Directory.Exists(plan.WorkingDirectory))
            psi.WorkingDirectory = plan.WorkingDirectory;
        if (plan.RunAsAdmin) psi.Verb = "runas";
        if (plan.HideWindow) psi.WindowStyle = ProcessWindowStyle.Hidden;
        using var p = Process.Start(psi);
        int pid = 0;
        try { pid = p?.Id ?? 0; } catch (InvalidOperationException) { }
        return LaunchResult.Ok(pid);
    }
}
