using DevPilot.Core.Guard;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class KillSwitchTests
{
    private static ToolEntry Py() => new() { Name = "T", Kind = ToolKind.Python, Path = @"C:\t\main.pyw" };

    [Fact]
    public void Not_paused_never_refuses()
    {
        Assert.Null(KillSwitch.Check(false, Py(), null));
    }

    [Fact]
    public void Paused_refuses_network_capable_tools_in_every_mode()
    {
        var d = KillSwitch.Check(true, Py(), null);
        Assert.NotNull(d);
        Assert.False(d!.CanStart);
        Assert.Equal(KillSwitch.PausedMessage, d.Message);
    }

    [Fact]
    public void Paused_leaves_tools_that_declared_no_external_access_and_non_process_tools_alone()
    {
        Assert.Null(KillSwitch.Check(true, Py(), new ToolNetworkPolicy { AllowNone = true }));
        Assert.Null(KillSwitch.Check(true, new ToolEntry { Kind = ToolKind.Folder, Path = @"C:\data" }, null));
    }

    [Fact]
    public void Approved_allowlist_decides_over_the_manifest()
    {
        var tool = Py();
        tool.GuardApproval = new GuardApproval { ManifestDigest = "X", AllowNone = true };
        Assert.False(KillSwitch.IsNetworkCapable(tool, new ToolNetworkPolicy { AllowedHosts = ["api.example.internal"] }));
        tool.GuardApproval = new GuardApproval { ManifestDigest = "X", AllowedHosts = ["api.example.internal"] };
        Assert.True(KillSwitch.IsNetworkCapable(tool, new ToolNetworkPolicy { AllowNone = true }));
    }

    [Fact]
    public void Pause_state_survives_a_config_round_trip()
    {
        var s = new AppSettings();
        Assert.False(s.Guard.Paused);
        s.Guard.Paused = true;
        var back = DevPilot.Core.Config.ConfigSerializer.Load(DevPilot.Core.Config.ConfigSerializer.Serialize(s), out _);
        Assert.True(back.Guard.Paused);
    }

    [Fact]
    public void Manifest_network_block_is_read_and_missing_manifest_gives_null()
    {
        using var tmp = new TempDir();
        var script = System.IO.Path.Combine(tmp.Path, "main.pyw");
        File.WriteAllText(script, "x");
        var tool = new ToolEntry { Kind = ToolKind.Python, Path = script };
        Assert.Null(GuardManifest.Network(tool));

        File.WriteAllText(System.IO.Path.Combine(tmp.Path, "devpilot.tool.json"), """{ "network": { "allowNone": true } }""");
        Assert.True(GuardManifest.Network(tool)!.AllowNone);

        File.WriteAllText(System.IO.Path.Combine(tmp.Path, "devpilot.tool.json"), "{ broken");
        Assert.Null(GuardManifest.Network(tool));
    }
}
