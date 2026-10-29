using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Net;
using DevPilot.Core.Guard;
using DevPilot.Core.Interfaces;
using DevPilot.Models;
using Microsoft.Extensions.Logging;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// Polls the TCP table for the processes DevPilot manages (and their children) and compares each new connection with
/// the tool's allowlist. Depending on the guard mode it logs, asks for a notification or stops the tool.
/// DevPilot only sees tools it launched; this is an audit aid and a guard rail, not a security boundary.
/// </summary>
public sealed class EgressMonitorService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan DnsTtl = TimeSpan.FromMinutes(10);

    private readonly IConfigStore _config;
    private readonly IProcessMonitor _processes;
    private readonly IConnectionSource _source;
    private readonly GuardEventLog _events;
    private readonly ILogger<EgressMonitorService> _logger;
    private readonly Dictionary<string, ConnectionTracker> _trackers = [];
    private readonly NotificationLimiter _limiter = new(TimeSpan.FromMinutes(10));
    private readonly ConcurrentDictionary<string, (string? Value, DateTime At)> _reverse = new();
    private readonly ConcurrentDictionary<string, (IReadOnlyCollection<string> Value, DateTime At)> _forward = new();
    private Timer? _timer;
    private int _busy;

    /// <summary>Raised (rate-limited) when a connection outside the allowlist is seen. Handlers must marshal to the UI thread.</summary>
    public event EventHandler<RecordedIncident>? IncidentRecorded;

    /// <summary>Folder for incident reports (disposable, local to this PC).</summary>
    public static string IncidentFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevPilot", "incidents");

    public EgressMonitorService(IConfigStore config, IProcessMonitor processes, IConnectionSource source,
        GuardEventLog events, ILogger<EgressMonitorService> logger)
    {
        _config = config;
        _processes = processes;
        _source = source;
        _events = events;
        _logger = logger;
    }

    public void Start() => _timer ??= new Timer(_ => Tick(), null, PollInterval, PollInterval);

    public void Dispose() => _timer?.Dispose();

    internal void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var mode = _config.Current.Guard.Mode;
            if (mode == EgressMode.Off) return;
            var running = _config.Current.Tools.ToList()
                .Where(ApprovalGate.Applies)
                .Select(t => (Tool: t, Pids: _processes.Get(t.Id).ProcessIds))
                .Where(x => x.Pids.Count > 0)
                .ToList();
            if (running.Count == 0) return;

            var connections = _source.Snapshot();
            var children = ChildMap();
            var local = LocalPolicy.Load();
            foreach (var (tool, roots) in running)
            {
                var tree = Descendants(roots, children);
                var mine = connections.Where(c => tree.Contains(c.Pid) && !ConnectionEvaluator.IsLocal(c.RemoteAddress));
                if (!_trackers.TryGetValue(tool.Id, out var tracker)) _trackers[tool.Id] = tracker = new ConnectionTracker();
                var fresh = tracker.NewOnes(mine);
                if (fresh.Count > 0) Handle(mode, tool, roots, fresh, local);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Egress monitor poll failed");
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private void Handle(EgressMode mode, ToolEntry tool, IReadOnlyList<int> roots, List<ConnectionInfo> fresh, LocalPolicy local)
    {
        var policy = ApprovalGate.PolicyFor(tool, GuardManifest.Network(tool));
        foreach (var c in fresh)
        {
            var host = ReverseLookup(c.RemoteAddress);
            var verdict = ConnectionEvaluator.Judge(policy, local, c.RemoteAddress, host, c.RemotePort, ForwardLookup);
            var decision = ConnectionEvaluator.Decide(mode, verdict);
            if (decision.Action == RuntimeAction.None) continue;

            var e = new GuardEvent(DateTime.UtcNow, tool.Name, host ?? c.RemoteAddress, c.RemoteAddress, c.RemotePort, verdict, decision.Action);
            _events.Add(e);
            _logger.LogWarning("Egress {Verdict}: {Tool} -> {Host}:{Port} ({Action})", verdict, tool.Name, e.Host, e.Port, decision.Action);

            var report = decision.Action is RuntimeAction.Notify or RuntimeAction.Stop
                && (decision.Action == RuntimeAction.Stop || _limiter.ShouldNotify($"{tool.Id}|{e.Host}|{e.Port}"));
            RecordedIncident? recorded = null;
            if (report)
            {
                var incident = new GuardIncident(e.TimeUtc, tool.Name, tool.Id, tool.Path, mode, decision.Action, verdict,
                    e.Host, e.Address, e.Port, roots, policy, _events.Recent(100));
                recorded = new RecordedIncident(incident, WriteReport(incident));
            }
            if (decision.Action == RuntimeAction.Stop)
            {
                // Blocked until reviewed: otherwise auto-restart would start the same tool again.
                ApprovalGate.Block(tool);
                _config.Save();
                Stop(tool, roots);
            }
            if (recorded is not null) IncidentRecorded?.Invoke(this, recorded);
            if (decision.Action == RuntimeAction.Stop) return;
        }
    }

    private string? WriteReport(GuardIncident incident)
    {
        try
        {
            Directory.CreateDirectory(IncidentFolder);
            var path = Path.Combine(IncidentFolder, IncidentReport.FileName(incident));
            File.WriteAllText(path, IncidentReport.Format(incident));
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Writing incident report failed");
            return null;
        }
    }

    private void Stop(ToolEntry tool, IReadOnlyList<int> roots)
    {
        foreach (var pid in roots)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _logger.LogDebug(ex, "Stopping pid {Pid} failed", pid);
            }
        }
        _logger.LogWarning("Egress Guard stopped {Tool}", tool.Name);
        _ = _processes.RefreshNowAsync();
    }

    // ── name lookups (cached; a slow or failing DNS never blocks the poll for long) ──

    private string? ReverseLookup(string address)
    {
        if (_reverse.TryGetValue(address, out var hit) && DateTime.UtcNow - hit.At < DnsTtl) return hit.Value;
        string? name = null;
        try
        {
            var task = Dns.GetHostEntryAsync(address);
            if (task.Wait(DnsTimeout)) name = task.Result.HostName;
        }
        catch (Exception ex) when (ex is AggregateException or System.Net.Sockets.SocketException) { }
        _reverse[address] = (name, DateTime.UtcNow);
        return name;
    }

    private IReadOnlyCollection<string> ForwardLookup(string host)
    {
        if (_forward.TryGetValue(host, out var hit) && DateTime.UtcNow - hit.At < DnsTtl) return hit.Value;
        IReadOnlyCollection<string> addresses = [];
        try
        {
            var task = Dns.GetHostAddressesAsync(host);
            if (task.Wait(DnsTimeout)) addresses = task.Result.Select(a => a.ToString()).ToList();
        }
        catch (Exception ex) when (ex is AggregateException or System.Net.Sockets.SocketException or ArgumentException) { }
        _forward[host] = (addresses, DateTime.UtcNow);
        return addresses;
    }

    // ── process tree ────────────────────────────────────────────────────

    private static Dictionary<int, List<int>> ChildMap()
    {
        var map = new Dictionary<int, List<int>>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId FROM Win32_Process");
            using var results = searcher.Get();
            foreach (ManagementObject mo in results)
            {
                using (mo)
                {
                    int pid = Convert.ToInt32(mo["ProcessId"]);
                    int parent = Convert.ToInt32(mo["ParentProcessId"]);
                    if (!map.TryGetValue(parent, out var list)) map[parent] = list = [];
                    list.Add(pid);
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // Without WMI only the tool's own processes are watched.
        }
        return map;
    }

    internal static HashSet<int> Descendants(IEnumerable<int> roots, Dictionary<int, List<int>> children)
    {
        var seen = new HashSet<int>(roots);
        var queue = new Queue<int>(seen);
        while (queue.Count > 0)
            if (children.TryGetValue(queue.Dequeue(), out var kids))
                foreach (var kid in kids)
                    if (seen.Add(kid)) queue.Enqueue(kid);
        return seen;
    }
}

/// <summary>An incident and the report file written for it (null when the file could not be written).</summary>
public sealed record RecordedIncident(GuardIncident Incident, string? FilePath);
