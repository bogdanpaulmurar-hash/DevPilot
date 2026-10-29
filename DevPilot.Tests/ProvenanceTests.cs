using DevPilot.Core.Guard;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class ProvenanceTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef01234567";
    private static string P(params string[] parts) => System.IO.Path.Combine(parts);

    private static void MakeRepo(string root, string remote = "https://example.com/team/tool.git", string head = "ref: refs/heads/main")
    {
        Directory.CreateDirectory(P(root, ".git", "refs", "heads"));
        File.WriteAllText(P(root, ".git", "HEAD"), head + "\n");
        File.WriteAllText(P(root, ".git", "refs", "heads", "main"), Hash + "\n");
        File.WriteAllText(P(root, ".git", "config"),
            $"[core]\n\tbare = false\n[remote \"origin\"]\n\turl = {remote}\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n");
    }

    [Fact]
    public void Reads_remote_and_commit_from_git_files()
    {
        using var tmp = new TempDir();
        MakeRepo(tmp.Path);
        var (remote, commit) = ProvenanceReader.ReadGit(tmp.Path);
        Assert.Equal("https://example.com/team/tool.git", remote);
        Assert.Equal(Hash, commit);
    }

    [Fact]
    public void Reads_commit_from_packed_refs_and_detached_head()
    {
        using var tmp = new TempDir();
        MakeRepo(tmp.Path);
        File.Delete(P(tmp.Path, ".git", "refs", "heads", "main"));
        File.WriteAllText(P(tmp.Path, ".git", "packed-refs"), $"# pack-refs\n{Hash} refs/heads/main\n");
        Assert.Equal(Hash, ProvenanceReader.ReadGit(tmp.Path).Commit);

        File.WriteAllText(P(tmp.Path, ".git", "HEAD"), Hash + "\n");
        Assert.Equal(Hash, ProvenanceReader.ReadGit(tmp.Path).Commit);
    }

    [Fact]
    public void Finds_git_in_a_parent_folder()
    {
        using var tmp = new TempDir();
        MakeRepo(tmp.Path);
        var sub = P(tmp.Path, "src", "tool");
        Directory.CreateDirectory(sub);
        Assert.Equal(Hash, ProvenanceReader.ReadGit(sub).Commit);
    }

    [Fact]
    public void Credentials_in_the_remote_are_never_kept()
    {
        using var tmp = new TempDir();
        MakeRepo(tmp.Path, remote: "https://user:secret-token@example.com/team/tool.git");
        var remote = ProvenanceReader.ReadGit(tmp.Path).Remote;
        Assert.Equal("https://example.com/team/tool.git", remote);
        Assert.DoesNotContain("secret-token", remote);
    }

    [Fact]
    public void Not_a_git_folder_gives_empty_values()
    {
        using var tmp = new TempDir();
        var (remote, commit) = ProvenanceReader.ReadGit(tmp.Path);
        Assert.Equal(string.Empty, remote);
        Assert.Equal(string.Empty, commit);
    }

    [Fact]
    public void Digest_is_stable_and_changes_with_content_name_or_new_files()
    {
        using var tmp = new TempDir();
        File.WriteAllText(P(tmp.Path, "a.py"), "print(1)");
        var first = ProvenanceReader.Read(tmp.Path);
        Assert.Equal(first.ManifestDigest, ProvenanceReader.Read(tmp.Path).ManifestDigest);
        Assert.Equal(1, first.FileCount);

        File.WriteAllText(P(tmp.Path, "a.py"), "print(2)");
        var changed = ProvenanceReader.Read(tmp.Path).ManifestDigest;
        Assert.NotEqual(first.ManifestDigest, changed);

        File.WriteAllText(P(tmp.Path, "b.txt"), "x");
        Assert.NotEqual(changed, ProvenanceReader.Read(tmp.Path).ManifestDigest);
    }

    [Fact]
    public void Digest_ignores_build_and_git_folders()
    {
        using var tmp = new TempDir();
        File.WriteAllText(P(tmp.Path, "a.py"), "x");
        var before = ProvenanceReader.Read(tmp.Path).ManifestDigest;
        MakeRepo(tmp.Path);
        Directory.CreateDirectory(P(tmp.Path, "bin"));
        File.WriteAllText(P(tmp.Path, "bin", "out.dll"), "junk");
        Assert.Equal(before, ProvenanceReader.Read(tmp.Path).ManifestDigest);
    }

    [Fact]
    public void Drift_detection()
    {
        using var tmp = new TempDir();
        MakeRepo(tmp.Path);
        File.WriteAllText(P(tmp.Path, "a.py"), "x");
        var now = ProvenanceReader.Read(tmp.Path);

        Assert.Equal(DriftState.NotApproved, GuardApprovals.CheckDrift(null, now).State);

        var approval = GuardApprovals.Create(now, EgressMode.Monitor,
            new ToolNetworkPolicy { AllowedHosts = ["api.example.internal"], Ports = [443] }, new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("monitor", approval.ApprovedMode);
        Assert.Equal(["api.example.internal"], approval.AllowedHosts);
        Assert.Equal(DriftState.Unchanged, GuardApprovals.CheckDrift(approval, now).State);

        File.WriteAllText(P(tmp.Path, "a.py"), "changed");
        var drift = GuardApprovals.CheckDrift(approval, ProvenanceReader.Read(tmp.Path));
        Assert.Equal(DriftState.Changed, drift.State);
        Assert.True(drift.NeedsReview);
        Assert.Contains(drift.Reasons, r => r.Contains("Files"));
    }

    [Fact]
    public void Remote_change_needs_review_even_if_files_are_the_same()
    {
        using var tmp = new TempDir();
        MakeRepo(tmp.Path);
        var approval = GuardApprovals.Create(ProvenanceReader.Read(tmp.Path), EgressMode.Monitor, null, DateTime.UtcNow);
        File.WriteAllText(P(tmp.Path, ".git", "config"), "[remote \"origin\"]\n\turl = https://other.example.com/x.git\n");
        var drift = GuardApprovals.CheckDrift(approval, ProvenanceReader.Read(tmp.Path));
        Assert.Equal(DriftState.Changed, drift.State);
        Assert.Contains(drift.Reasons, r => r.Contains("remote"));
    }

    [Fact]
    public void Approval_survives_config_round_trip()
    {
        var settings = new AppSettings();
        settings.Tools.Add(new ToolEntry { Name = "T", GuardApproval = new GuardApproval { ManifestDigest = "ABC", FileCount = 3, AllowNone = true } });
        var json = DevPilot.Core.Config.ConfigSerializer.Serialize(settings);
        var back = DevPilot.Core.Config.ConfigSerializer.Load(json, out _);
        var a = back.Tools.Single(t => t.Name == "T").GuardApproval!;
        Assert.Equal("ABC", a.ManifestDigest);
        Assert.True(a.AllowNone);
    }
}
