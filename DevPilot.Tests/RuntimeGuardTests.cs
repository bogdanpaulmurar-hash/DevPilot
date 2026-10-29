using System.Net;
using System.Net.Sockets;
using DevPilot.Core.Guard;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class RuntimeGuardTests
{
    private static readonly LocalPolicy NoLocal = new();
    private static ToolNetworkPolicy Policy(params string[] hosts) => new() { AllowedHosts = [.. hosts] };

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("203.0.113.7", false)]
    [InlineData("not-an-ip", false)]
    public void Local_addresses(string address, bool local) =>
        Assert.Equal(local, ConnectionEvaluator.IsLocal(address));

    [Fact]
    public void Judge_allows_by_name_by_address_and_by_forward_lookup()
    {
        var p = Policy("api.example.internal", "*.cdn.example.internal", "203.0.113.9");
        Assert.Equal(ConnectionVerdict.Allowed, ConnectionEvaluator.Judge(p, NoLocal, "198.51.100.1", "api.example.internal", 443));
        Assert.Equal(ConnectionVerdict.Allowed, ConnectionEvaluator.Judge(p, NoLocal, "198.51.100.1", "x.cdn.example.internal", 443));
        Assert.Equal(ConnectionVerdict.Allowed, ConnectionEvaluator.Judge(p, NoLocal, "203.0.113.9", null, 443));
        Assert.Equal(ConnectionVerdict.Allowed, ConnectionEvaluator.Judge(p, NoLocal, "198.51.100.77", "ec2-odd-name.example.test", 443,
            host => host == "api.example.internal" ? new[] { "198.51.100.77" } : Array.Empty<string>()));
        Assert.Equal(ConnectionVerdict.NotAllowed, ConnectionEvaluator.Judge(p, NoLocal, "198.51.100.5", "other.example.test", 443));
    }

    [Fact]
    public void Judge_checks_ports_and_allow_none()
    {
        var p = new ToolNetworkPolicy { AllowedHosts = ["api.example.internal"], Ports = [443] };
        Assert.Equal(ConnectionVerdict.NotAllowed, ConnectionEvaluator.Judge(p, NoLocal, "198.51.100.1", "api.example.internal", 80));
        var none = new ToolNetworkPolicy { AllowNone = true };
        Assert.Equal(ConnectionVerdict.NotAllowed, ConnectionEvaluator.Judge(none, NoLocal, "198.51.100.1", "api.example.internal", 443));
        Assert.Equal(ConnectionVerdict.Allowed, ConnectionEvaluator.Judge(none, NoLocal, "127.0.0.1", null, 5000));
    }

    [Fact]
    public void Judge_without_declared_allowlist_is_no_policy_and_local_always_allowed_hosts_win()
    {
        Assert.Equal(ConnectionVerdict.NoPolicy, ConnectionEvaluator.Judge(null, NoLocal, "198.51.100.1", null, 443));
        Assert.Equal(ConnectionVerdict.NoPolicy, ConnectionEvaluator.Judge(new ToolNetworkPolicy(), NoLocal, "198.51.100.1", null, 443));
        var local = new LocalPolicy { AlwaysAllowedHosts = ["proxy.example.internal"] };
        Assert.Equal(ConnectionVerdict.Allowed, ConnectionEvaluator.Judge(Policy("api.example.internal"), local, "198.51.100.1", "proxy.example.internal", 8080));
    }

    [Theory]
    [InlineData(EgressMode.Off, ConnectionVerdict.NotAllowed, RuntimeAction.None)]
    [InlineData(EgressMode.Monitor, ConnectionVerdict.Allowed, RuntimeAction.None)]
    [InlineData(EgressMode.Monitor, ConnectionVerdict.NotAllowed, RuntimeAction.Notify)]
    [InlineData(EgressMode.Monitor, ConnectionVerdict.NoPolicy, RuntimeAction.Log)]
    [InlineData(EgressMode.Block, ConnectionVerdict.NotAllowed, RuntimeAction.Stop)]
    [InlineData(EgressMode.Block, ConnectionVerdict.NoPolicy, RuntimeAction.Log)]
    [InlineData(EgressMode.Block, ConnectionVerdict.Allowed, RuntimeAction.None)]
    public void Decide_by_mode(EgressMode mode, ConnectionVerdict verdict, RuntimeAction expected) =>
        Assert.Equal(expected, ConnectionEvaluator.Decide(mode, verdict).Action);

    [Fact]
    public void Tracker_reports_each_connection_once_and_forgets_closed_ones()
    {
        var t = new ConnectionTracker();
        var a = new ConnectionInfo(1, "198.51.100.1", 443, 50000);
        var b = new ConnectionInfo(1, "198.51.100.2", 443, 50001);
        Assert.Equal(a, Assert.Single(t.NewOnes(new[] { a })));
        Assert.Equal(b, Assert.Single(t.NewOnes(new[] { a, b })));
        Assert.Empty(t.NewOnes(new[] { a, b }));
        Assert.Empty(t.NewOnes(Array.Empty<ConnectionInfo>()));
        Assert.Equal(a, Assert.Single(t.NewOnes(new[] { a })));
    }

    [Fact]
    public void Limiter_allows_one_notification_per_key_and_window()
    {
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var l = new NotificationLimiter(TimeSpan.FromMinutes(10), () => now);
        Assert.True(l.ShouldNotify("a"));
        Assert.False(l.ShouldNotify("a"));
        Assert.True(l.ShouldNotify("b"));
        now = now.AddMinutes(11);
        Assert.True(l.ShouldNotify("a"));
    }

    [Fact]
    public void Event_log_keeps_the_newest_and_raises_added()
    {
        var log = new GuardEventLog();
        int raised = 0;
        log.Added += (_, _) => raised++;
        for (int i = 0; i < GuardEventLog.Capacity + 10; i++)
            log.Add(new GuardEvent(DateTime.UtcNow, "T", "h" + i, "a", i, ConnectionVerdict.NotAllowed, RuntimeAction.Notify));
        Assert.Equal(GuardEventLog.Capacity + 10, raised);
        var recent = log.Recent();
        Assert.Equal(GuardEventLog.Capacity, recent.Count);
        Assert.Equal("h" + (GuardEventLog.Capacity + 9), recent[^1].Host);
        Assert.Single(log.Recent(1));
        log.Clear();
        Assert.Empty(log.Recent());
    }

    [Fact]
    public void Approved_allowlist_wins_over_the_manifest()
    {
        var tool = new ToolEntry { Kind = ToolKind.Python };
        var manifest = new ToolNetworkPolicy { AllowedHosts = ["wider.example.internal"] };
        Assert.Same(manifest, ApprovalGate.PolicyFor(tool, manifest));

        ApprovalGate.Approve(tool, new Provenance("", "", "AAA", 1, false), EgressMode.Monitor,
            new ToolNetworkPolicy { AllowedHosts = ["api.example.internal"], Ports = [443] }, DateTime.UtcNow);
        var effective = ApprovalGate.PolicyFor(tool, manifest)!;
        Assert.Equal("api.example.internal", Assert.Single(effective.AllowedHosts));
        Assert.Equal(443, Assert.Single(effective.Ports));
    }

    [Fact]
    public void Tcp_table_shows_a_connection_this_process_opened()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);

        var all = new TcpTableReader().Snapshot();
        Assert.Contains(all, c => c.Pid == Environment.ProcessId && c.RemoteAddress == "127.0.0.1" && c.RemotePort == port);
    }
}
