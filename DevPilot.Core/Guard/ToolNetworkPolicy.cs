namespace DevPilot.Core.Guard;

/// <summary>
/// The optional <c>network</c> block of <c>devpilot.tool.json</c>: which hosts and ports a tool may reach.
/// A tool without the block has no declared allowlist (<see cref="IsDeclared"/> is false).
/// </summary>
public sealed class ToolNetworkPolicy
{
    /// <summary>Host names, with an optional leading <c>*.</c> wildcard (matches subdomains only, not the bare domain).</summary>
    public List<string> AllowedHosts { get; set; } = [];

    /// <summary>Allowed ports; empty = any port on an allowed host.</summary>
    public List<int> Ports { get; set; } = [];

    /// <summary>The tool declares that it makes no external connections at all.</summary>
    public bool AllowNone { get; set; }

    public bool IsDeclared => AllowNone || AllowedHosts.Count > 0 || Ports.Count > 0;

    public bool PortAllowed(int port) => Ports.Count == 0 || Ports.Contains(port);

    public bool Allows(string host, int port)
    {
        if (AllowNone) return false;
        if (Ports.Count > 0 && !Ports.Contains(port)) return false;
        if (AllowedHosts.Count == 0) return false;
        return AllowedHosts.Any(pattern => HostMatches(pattern, host));
    }

    internal static bool HostMatches(string pattern, string host)
    {
        pattern = pattern.Trim().TrimEnd('.');
        host = host.Trim().TrimEnd('.');
        if (pattern.Length == 0 || host.Length == 0) return false;
        if (pattern == "*") return true;
        if (pattern.StartsWith("*.", StringComparison.Ordinal))
            return host.Length > pattern.Length - 1
                   && host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        return string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);
    }
}
