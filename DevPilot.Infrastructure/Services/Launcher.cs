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

    /// <summary>How long a watched start may take to fail before it counts as started.</summary>
    internal static TimeSpan StartupWatch { get; set; } = TimeSpan.FromSeconds(2.5);

    /// <summary>Processes whose stderr is still being drained (a closed pipe could break them later).</summary>
    private static readonly HashSet<Process> Watched = [];

    private static LaunchResult StartProcess(LaunchPlan plan, string fileName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = plan.Arguments,
            UseShellExecute = false,
            CreateNoWindow = plan.HideWindow,
            RedirectStandardError = plan.WatchStartup,
        };
        if (!string.IsNullOrWhiteSpace(plan.WorkingDirectory) && Directory.Exists(plan.WorkingDirectory))
            psi.WorkingDirectory = plan.WorkingDirectory;
        foreach (var (k, v) in plan.Environment) psi.Environment[k] = v;
        if (!plan.WatchStartup)
        {
            using var p = Process.Start(psi);
            return LaunchResult.Ok(p?.Id ?? 0);
        }
        return StartWatched(psi);
    }

    /// <summary>
    /// Starts a windowless (pythonw) process and waits briefly: if it ends with an error right away
    /// (wrong interpreter, missing module), the last stderr line becomes the launch error instead of a silent failure.
    /// </summary>
    private static LaunchResult StartWatched(ProcessStartInfo psi)
    {
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var tail = new Queue<string>();
        p.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (tail)
            {
                tail.Enqueue(e.Data.Trim());
                while (tail.Count > 20) tail.Dequeue();
            }
        };
        if (!p.Start()) { p.Dispose(); return LaunchResult.Fail("The process did not start."); }
        p.BeginErrorReadLine();
        int pid = p.Id;
        if (!p.WaitForExit((int)StartupWatch.TotalMilliseconds))
        {
            // Still running: keep draining stderr until it exits, then release it.
            lock (Watched) Watched.Add(p);
            p.Exited += (_, _) => { lock (Watched) Watched.Remove(p); p.Dispose(); };
            if (p.HasExited) { lock (Watched) Watched.Remove(p); }
            return LaunchResult.Ok(pid);
        }
        p.WaitForExit(); // flushes the asynchronous stderr reader
        int code = p.ExitCode;
        string[] lines;
        lock (tail) lines = tail.ToArray();
        p.Dispose();
        if (code == 0) return LaunchResult.Ok(pid);
        return LaunchResult.Fail(DescribeEarlyExit(code, lines));
    }

    internal static string DescribeEarlyExit(int code, IReadOnlyList<string> stderr)
    {
        var last = stderr.LastOrDefault(l => !l.StartsWith("File ", StringComparison.Ordinal) && !l.StartsWith("^", StringComparison.Ordinal))
                   ?? stderr.LastOrDefault() ?? string.Empty;
        var message = string.IsNullOrEmpty(last) ? $"It stopped right away (exit code {code})." : $"It stopped right away: {last}";
        if (stderr.Any(l => l.Contains("No module named", StringComparison.Ordinal)))
            message += " — this Python does not have the tool installed. Set the tool's Interpreter field to the pythonw.exe of its venv.";
        return message;
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
