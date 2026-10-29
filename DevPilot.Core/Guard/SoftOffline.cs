namespace DevPilot.Core.Guard;

/// <summary>
/// "Soft offline" launch: the proxy variables point at a local address nothing listens on, and NO_PROXY is empty so
/// nothing bypasses it. It only affects tools and libraries that honour these variables (most HTTP clients do, raw
/// sockets and tools with their own proxy settings do not), so it is a convenience, not a security boundary.
/// </summary>
public static class SoftOffline
{
    /// <summary>Port 9 (discard) on loopback: connections are refused at once.</summary>
    public const string DeadProxy = "http://127.0.0.1:9";

    public const string EnvOffline = "DEVPILOT_OFFLINE";

    public static void Apply(IDictionary<string, string> env)
    {
        env["HTTP_PROXY"] = DeadProxy;
        env["HTTPS_PROXY"] = DeadProxy;
        env["ALL_PROXY"] = DeadProxy;
        env["NO_PROXY"] = string.Empty;
        env[EnvOffline] = "1";
    }
}
