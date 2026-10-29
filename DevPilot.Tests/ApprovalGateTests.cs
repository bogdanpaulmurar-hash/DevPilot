using DevPilot.Core.Guard;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class ApprovalGateTests
{
    private static Provenance Prov(string digest = "AAA", string remote = "") => new(remote, "", digest, 1, false);
    private static ToolEntry Tool() => new() { Name = "T", Kind = ToolKind.Python, Path = @"C:\tools\t\main.pyw" };

    [Fact]
    public void Off_checks_nothing_and_never_hashes()
    {
        var d = ApprovalGate.Evaluate(EgressMode.Off, Tool(), () => throw new InvalidOperationException("must not hash"));
        Assert.True(d.CanStart);
        Assert.False(d.Checked);
    }

    [Fact]
    public void Tools_that_start_no_process_are_not_gated()
    {
        var folder = new ToolEntry { Kind = ToolKind.Folder, Path = @"C:\data" };
        var d = ApprovalGate.Evaluate(EgressMode.Block, folder, () => throw new InvalidOperationException("must not hash"));
        Assert.True(d.CanStart);
        Assert.False(d.Checked);
    }

    [Fact]
    public void Monitor_always_starts_but_warns_until_approved()
    {
        var tool = Tool();
        var first = ApprovalGate.Evaluate(EgressMode.Monitor, tool, () => Prov());
        Assert.True(first.CanStart);
        Assert.Equal(GuardStatus.Unreviewed, first.Status);
        Assert.NotEmpty(first.Message);

        Assert.True(ApprovalGate.Approve(tool, Prov(), EgressMode.Monitor, null, DateTime.UtcNow));
        var approved = ApprovalGate.Evaluate(EgressMode.Monitor, tool, () => Prov());
        Assert.True(approved.CanStart);
        Assert.Equal(GuardStatus.Approved, approved.Status);
        Assert.Equal(string.Empty, approved.Message);
    }

    [Fact]
    public void Block_mode_starts_only_approved_unchanged_tools()
    {
        var tool = Tool();
        Assert.False(ApprovalGate.Evaluate(EgressMode.Block, tool, () => Prov()).CanStart);

        ApprovalGate.Approve(tool, Prov(), EgressMode.Block, null, DateTime.UtcNow);
        Assert.True(ApprovalGate.Evaluate(EgressMode.Block, tool, () => Prov()).CanStart);

        var drifted = ApprovalGate.Evaluate(EgressMode.Block, tool, () => Prov("BBB"));
        Assert.False(drifted.CanStart);
        Assert.Equal(GuardStatus.Drifted, drifted.Status);
    }

    [Fact]
    public void Blocked_tool_does_not_start_in_block_mode_even_if_approved_before()
    {
        var tool = Tool();
        ApprovalGate.Approve(tool, Prov(), EgressMode.Block, null, DateTime.UtcNow);
        ApprovalGate.Block(tool);
        var d = ApprovalGate.Evaluate(EgressMode.Block, tool, () => Prov());
        Assert.False(d.CanStart);
        Assert.Equal(GuardStatus.Blocked, d.Status);
        Assert.True(ApprovalGate.Evaluate(EgressMode.Monitor, tool, () => Prov()).CanStart);
    }

    [Fact]
    public void Approving_clears_a_block_and_reset_goes_back_to_unreviewed()
    {
        var tool = Tool();
        ApprovalGate.Block(tool);
        ApprovalGate.Approve(tool, Prov(), EgressMode.Block, null, DateTime.UtcNow);
        Assert.False(tool.GuardBlocked);
        Assert.Equal(GuardStatus.Approved, ApprovalGate.StatusOf(tool, Prov()));

        ApprovalGate.Reset(tool);
        Assert.Null(tool.GuardApproval);
        Assert.Equal(GuardStatus.Unreviewed, ApprovalGate.StatusOf(tool, Prov()));
    }

    [Fact]
    public void Nothing_to_hash_cannot_be_approved()
    {
        var tool = Tool();
        Assert.False(ApprovalGate.Approve(tool, Prov(digest: ""), EgressMode.Block, null, DateTime.UtcNow));
        Assert.Null(tool.GuardApproval);
    }

    [Fact]
    public void Messages_carry_no_policy_details()
    {
        var tool = Tool();
        var d = ApprovalGate.Evaluate(EgressMode.Block, tool, () => Prov());
        Assert.DoesNotContain("\\", d.Message);
    }

    [Fact]
    public void Default_guard_mode_is_monitor_and_survives_a_config_round_trip()
    {
        var s = new AppSettings();
        Assert.Equal(EgressMode.Monitor, s.Guard.Mode);
        s.Guard.Mode = EgressMode.Block;
        var back = DevPilot.Core.Config.ConfigSerializer.Load(DevPilot.Core.Config.ConfigSerializer.Serialize(s), out _);
        Assert.Equal(EgressMode.Block, back.Guard.Mode);
    }

    [Fact]
    public void Single_program_file_is_hashed_when_its_folder_is_shared()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.True(ProvenanceReader.IsSharedFolder(windows));
        using var tmp = new TempDir();
        Assert.False(ProvenanceReader.IsSharedFolder(tmp.Path));

        var exe = System.IO.Path.Combine(tmp.Path, "tool.pyw");
        File.WriteAllText(exe, "x");
        var tool = new ToolEntry { Kind = ToolKind.Python, Path = exe, WorkingDirectory = windows };
        var p = ProvenanceReader.ReadForTool(tool);
        Assert.Equal(1, p.FileCount);
        File.WriteAllText(exe, "y");
        Assert.NotEqual(p.ManifestDigest, ProvenanceReader.ReadForTool(tool).ManifestDigest);
    }
}
