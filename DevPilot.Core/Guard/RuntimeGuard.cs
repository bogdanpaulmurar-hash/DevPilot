using System.Net;
using DevPilot.Models;

namespace DevPilot.Core.Guard;

/// <summary>An outbound TCP connection owned by a process (UDP has no remote endpoint to look at).</summary>
public sealed record ConnectionInfo(int Pid, string RemoteAddress, int RemotePort, int LocalPort);

public enum ConnectionVerdict { Allowed, NotAllowed, NoPolicy }

public enum RuntimeAction { None, Log, Notify, Stop }

public sealed record RuntimeDecision(ConnectionVerdict Verdict, RuntimeAction Action);

/// <summary>One thing the runtime monitor noticed. Kept in memory for the Guard page; the connection target is the tool's own.</summary>
public sealed record GuardEvent(DateTime TimeUtc, string ToolName, string Host, string Address, int Port,
    ConnectionVerdict Verdict, RuntimeAction Action);

public static class ConnectionEvaluator
{
    /// <summary>Loopback and "any" addresses never leave the machine.</summary>
    public static bool IsLocal(string address) =>
        IPAddress.TryParse(address, out var ip) && (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any));

    /// <param name="host">Reverse-resolved name of <paramref name="address"/>, if any.</param>
    /// <param name="forward">Optional forward lookup (name to addresses) so a declared host also matches by IP.</param>
    public static ConnectionVerdict Judge(ToolNetworkPolicy? policy, LocalPolicy local, string address, string? host, int port,
        Func<string, IReadOnlyCollection<string>>? forward = null)
    {
        if (IsLocal(address)) return ConnectionVerdict.Allowed;
        if (local.IsAlwaysAllowed(address) || (host is not null && local.IsAlwaysAllowed(host))) return ConnectionVerdict.Allowed;
        if (policy is null || !policy.IsDeclared) return ConnectionVerdict.NoPolicy;
        if (policy.AllowNone || !policy.PortAllowed(port)) return ConnectionVerdict.NotAllowed;

        foreach (var pattern in policy.AllowedHosts)
        {
            if (ToolNetworkPolicy.HostMatches(pattern, address)) return ConnectionVerdict.Allowed;
            if (host is not null && ToolNetworkPolicy.HostMatches(pattern, host)) return ConnectionVerdict.Allowed;
            if (forward is not null && pattern != "*" && !pattern.StartsWith("*.", StringComparison.Ordinal)
                && forward(pattern).Contains(address)) return ConnectionVerdict.Allowed;
        }
        return ConnectionVerdict.NotAllowed;
    }

    /// <summary>
    /// What to do with a verdict. A tool that declares no allowlist is only logged, in every mode: DevPilot never
    /// stops a tool for something the tool never promised. Blocking needs a declared allowlist.
    /// </summary>
    public static RuntimeDecision Decide(EgressMode mode, ConnectionVerdict verdict)
    {
        if (mode == EgressMode.Off || verdict == ConnectionVerdict.Allowed) return new(verdict, RuntimeAction.None);
        if (verdict == ConnectionVerdict.NoPolicy) return new(verdict, RuntimeAction.Log);
        return new(verdict, mode == EgressMode.Block ? RuntimeAction.Stop : RuntimeAction.Notify);
    }
}

/// <summary>Reports each connection once while it lasts, so a long-lived connection is not logged every poll.</summary>
public sealed class ConnectionTracker
{
    private readonly HashSet<string> _seen = [];

    private static string Key(ConnectionInfo c) => $"{c.Pid}|{c.RemoteAddress}|{c.RemotePort}|{c.LocalPort}";

    /// <summary>Connections in <paramref name="current"/> not reported before; forgets the ones that are gone.</summary>
    public List<ConnectionInfo> NewOnes(IEnumerable<ConnectionInfo> current)
    {
        var list = current.ToList();
        var keys = list.Select(Key).ToHashSet();
        _seen.IntersectWith(keys);
        var fresh = new List<ConnectionInfo>();
        foreach (var c in list)
            if (_seen.Add(Key(c))) fresh.Add(c);
        return fresh;
    }
}

/// <summary>At most one notification per key within a time window.</summary>
public sealed class NotificationLimiter
{
    private readonly TimeSpan _window;
    private readonly Func<DateTime> _now;
    private readonly Dictionary<string, DateTime> _last = [];

    public NotificationLimiter(TimeSpan window, Func<DateTime>? now = null)
    {
        _window = window;
        _now = now ?? (() => DateTime.UtcNow);
    }

    public bool ShouldNotify(string key)
    {
        var now = _now();
        if (_last.TryGetValue(key, out var at) && now - at < _window) return false;
        _last[key] = now;
        return true;
    }
}

/// <summary>The most recent guard events, newest last. Thread-safe; fixed size.</summary>
public sealed class GuardEventLog
{
    public const int Capacity = 500;
    private readonly object _gate = new();
    private readonly List<GuardEvent> _events = [];

    public event EventHandler<GuardEvent>? Added;

    public void Add(GuardEvent e)
    {
        lock (_gate)
        {
            _events.Add(e);
            if (_events.Count > Capacity) _events.RemoveRange(0, _events.Count - Capacity);
        }
        Added?.Invoke(this, e);
    }

    public IReadOnlyList<GuardEvent> Recent(int count = Capacity)
    {
        lock (_gate) return _events.Skip(Math.Max(0, _events.Count - count)).ToList();
    }

    public void Clear()
    {
        lock (_gate) _events.Clear();
    }
}
