using DevPilot.Core.Guard;
using DevPilot.Core.Presets;
using Xunit;

namespace DevPilot.Tests;

public class GuardModelTests
{
    [Theory]
    [InlineData("api.example.internal", "api.example.internal", true)]
    [InlineData("API.Example.Internal", "api.example.internal", true)]
    [InlineData("*.example.internal", "api.example.internal", true)]
    [InlineData("*.example.internal", "example.internal", false)]
    [InlineData("*.example.internal", "badexample.internal", false)]
    [InlineData("example.internal", "api.example.internal", false)]
    [InlineData("*", "anything.test", true)]
    [InlineData("", "anything.test", false)]
    public void Host_patterns(string pattern, string host, bool expected) =>
        Assert.Equal(expected, ToolNetworkPolicy.HostMatches(pattern, host));

    [Fact]
    public void Allows_checks_host_and_port()
    {
        var p = new ToolNetworkPolicy { AllowedHosts = ["api.example.internal"], Ports = [443] };
        Assert.True(p.Allows("api.example.internal", 443));
        Assert.False(p.Allows("api.example.internal", 80));
        Assert.False(p.Allows("other.example.internal", 443));
    }

    [Fact]
    public void AllowNone_blocks_everything()
    {
        var p = new ToolNetworkPolicy { AllowNone = true, AllowedHosts = ["*"] };
        Assert.False(p.Allows("api.example.internal", 443));
        Assert.True(p.IsDeclared);
    }

    [Fact]
    public void Empty_policy_is_not_declared_and_allows_nothing()
    {
        var p = new ToolNetworkPolicy();
        Assert.False(p.IsDeclared);
        Assert.False(p.Allows("api.example.internal", 443));
    }

    [Fact]
    public void Report_counts_and_worst_severity()
    {
        var r = new GuardReport();
        Assert.Null(r.Worst);
        r.Findings.Add(new GuardFinding(GuardSeverity.Info, "a", "f.cs", 1, "m"));
        r.Findings.Add(new GuardFinding(GuardSeverity.High, "b", "f.cs", 2, "m"));
        Assert.Equal(GuardSeverity.High, r.Worst);
        Assert.Equal(1, r.Count(GuardSeverity.Info));
        Assert.Equal(0, r.Count(GuardSeverity.Warn));
    }

    [Fact]
    public void Missing_local_policy_is_empty()
    {
        using var tmp = new TempDir();
        var p = LocalPolicy.Load(Path.Combine(tmp.Path, LocalPolicy.FileName));
        Assert.Empty(p.DenyTerms);
        Assert.Null(p.LoadProblem);
        Assert.False(p.Contains("anything"));
    }

    [Fact]
    public void Local_policy_matches_terms_without_listing_them()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, LocalPolicy.FileName);
        File.WriteAllText(file, """{ "denyTerms": [" Project-X ", ""], "alwaysAllowedHosts": ["proxy.example.internal"] }""");
        var p = LocalPolicy.Load(file);
        Assert.True(p.Contains("see project-x notes"));
        Assert.False(p.Contains("nothing here"));
        Assert.True(p.IsAlwaysAllowed("proxy.example.internal"));
        Assert.DoesNotContain("Project-X", p.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Broken_local_policy_is_ignored_and_reported_without_content()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, LocalPolicy.FileName);
        File.WriteAllText(file, "{ not json secret-term");
        var p = LocalPolicy.Load(file);
        Assert.Empty(p.DenyTerms);
        Assert.NotNull(p.LoadProblem);
        Assert.DoesNotContain("secret-term", p.LoadProblem!);
    }

    [Fact]
    public void Manifest_network_block_is_optional_and_parsed()
    {
        using var tmp = new TempDir();
        var plain = Path.Combine(tmp.Path, "a.json");
        File.WriteAllText(plain, """{ "name": "Tool" }""");
        Assert.Null(ToolManifest.Load(plain).Network);

        var withNet = Path.Combine(tmp.Path, "b.json");
        File.WriteAllText(withNet, """{ "name": "Tool", "network": { "allowedHosts": ["*.example.internal"], "ports": [443] } }""");
        var net = ToolManifest.Load(withNet).Network!;
        Assert.True(net.Allows("api.example.internal", 443));
    }
}
