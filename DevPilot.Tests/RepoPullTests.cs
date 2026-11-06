using DevPilot.Core.Interfaces;
using DevPilot.Core.Repos;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public sealed class RepoPullTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "devpilot-repos-" + Guid.NewGuid().ToString("N"));

    public RepoPullTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            // git marks pack files read-only; clear that so the folder can be deleted.
            foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_root, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private string Dir(string relative)
    {
        var d = Path.Combine(_root, relative);
        Directory.CreateDirectory(d);
        return d;
    }

    private string FakeRepo(string relative)
    {
        var d = Dir(relative);
        Directory.CreateDirectory(Path.Combine(d, ".git"));
        File.WriteAllText(Path.Combine(d, ".git", "HEAD"), "ref: refs/heads/main\n");
        return d;
    }

    // ── Scanner ─────────────────────────────────────────────────────────

    [Fact]
    public void Scanner_finds_repos_but_not_inside_them_nor_in_skipped_folders()
    {
        var a = FakeRepo("ToolA");
        var b = FakeRepo(@"group\ToolB");
        FakeRepo(@"ToolA\vendor\inner");
        FakeRepo(@"node_modules\pkg");
        FakeRepo(@".hidden\x");
        Dir("NotARepo");
        var found = RepoScanner.Find(_root);
        Assert.Equal(new[] { b, a }, found); // sorted: "group" before "ToolA"
    }

    [Fact]
    public void Scanner_returns_the_root_when_it_is_a_repo_and_nothing_for_a_missing_folder()
    {
        var a = FakeRepo("Single");
        Assert.Equal(new[] { a }, RepoScanner.Find(a));
        Assert.Empty(RepoScanner.Find(Path.Combine(_root, "missing")));
        Assert.Empty(RepoScanner.Find(""));
    }

    [Fact]
    public void Scanner_stops_at_max_depth()
    {
        FakeRepo(@"a\b\c\deep");
        var shallow = FakeRepo(@"a\b\ok");
        Assert.Equal(new[] { shallow }, RepoScanner.Find(_root));
    }

    [Fact]
    public void Worktree_git_file_counts_as_repo_and_branch_is_read()
    {
        var real = FakeRepo("main-clone");
        var wtGit = Path.Combine(real, ".git", "worktrees", "wt");
        Directory.CreateDirectory(wtGit);
        File.WriteAllText(Path.Combine(wtGit, "HEAD"), "ref: refs/heads/feature/x\n");
        var wt = Dir("wt");
        File.WriteAllText(Path.Combine(wt, ".git"), "gitdir: " + wtGit + "\n");
        Assert.True(RepoScanner.IsRepo(wt));
        Assert.Equal("feature/x", RepoScanner.ReadBranch(wt));
        Assert.Equal("main", RepoScanner.ReadBranch(real));
        File.WriteAllText(Path.Combine(real, ".git", "HEAD"), "0123456789abcdef0123456789abcdef01234567\n");
        Assert.Equal("detached", RepoScanner.ReadBranch(real));
    }

    [Fact]
    public void Display_name_is_relative_to_root()
    {
        Assert.Equal(@"group\ToolB", RepoScanner.DisplayName(@"G:\Dev", @"G:\Dev\group\ToolB"));
        Assert.Equal("ToolB", RepoScanner.DisplayName(@"C:\Other", @"G:\Dev\ToolB"));
        Assert.True(RepoScanner.SamePath(@"G:\Dev\ToolB\", @"g:\dev\toolb"));
    }

    // ── Classification ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "Already up to date.\n", "", RepoPullStatus.UpToDate)]
    [InlineData(0, "Updating 1a2b..3c4d\nFast-forward\n a.txt | 2 +-\n 1 file changed, 1 insertion(+), 1 deletion(-)\n", "", RepoPullStatus.Updated)]
    [InlineData(1, "", "There is no tracking information for the current branch.\nPlease specify which branch you want to merge with.", RepoPullStatus.NoUpstream)]
    [InlineData(1, "", "You are not currently on a branch.", RepoPullStatus.NoUpstream)]
    [InlineData(128, "", "hint: Diverging branches can't be fast-forwarded\nfatal: Not possible to fast-forward, aborting.", RepoPullStatus.Diverged)]
    [InlineData(1, "Updating 1a..2b\n", "error: Your local changes to the following files would be overwritten by merge:\n\ta.txt\nPlease commit your changes or stash them before you merge.\nAborting", RepoPullStatus.LocalChanges)]
    [InlineData(128, "", "fatal: detected dubious ownership in repository at 'G:/My Drive/Dev/x'\nTo add an exception for this directory, call:\n\n\tgit config --global --add safe.directory 'G:/My Drive/Dev/x'", RepoPullStatus.UnsafeOwnership)]
    [InlineData(128, "", "fatal: could not read Username for 'https://github.com': terminal prompts disabled", RepoPullStatus.AuthFailed)]
    [InlineData(128, "", "git@github.com: Permission denied (publickey).\nfatal: Could not read from remote repository.", RepoPullStatus.AuthFailed)]
    [InlineData(128, "", "fatal: unable to access 'https://github.com/x/y.git/': Could not resolve host: github.com", RepoPullStatus.Offline)]
    [InlineData(128, "", "fatal: not a git repository (or any of the parent directories): .git", RepoPullStatus.NotARepo)]
    [InlineData(1, "", "error: unable to unlink old 'a.txt': Permission denied", RepoPullStatus.Failed)]
    public void Pull_output_is_classified(int exit, string stdout, string stderr, RepoPullStatus expected) =>
        Assert.Equal(expected, RepoPull.Classify(exit, stdout, stderr));

    [Fact]
    public void Porcelain_counts_tracked_changes_only()
    {
        Assert.Equal(0, RepoPull.CountChanges(""));
        Assert.Equal(0, RepoPull.CountChanges("?? new.txt\n"));
        Assert.Equal(2, RepoPull.CountChanges(" M a.txt\r\nA  b.txt\r\n?? c.txt\r\n"));
    }

    [Fact]
    public void Updated_message_is_the_change_line_and_failure_message_skips_hints()
    {
        Assert.Equal("1 file changed, 1 insertion(+)", RepoPull.Describe(RepoPullStatus.Updated, "Fast-forward\n a | 1 +\n 1 file changed, 1 insertion(+)\n", ""));
        Assert.Equal("fatal: boom", RepoPull.Describe(RepoPullStatus.Failed, "", "fatal: boom\nhint: try again\n"));
    }

    [Fact]
    public void Summary_counts_each_kind()
    {
        RepoPullResult R(RepoPullStatus s) => new("x", s, "", DateTime.Now);
        var results = new[] { R(RepoPullStatus.Updated), R(RepoPullStatus.UpToDate), R(RepoPullStatus.UpToDate), R(RepoPullStatus.LocalChanges), R(RepoPullStatus.Offline) };
        Assert.Equal("1 updated · 2 up to date · 1 skipped · 1 failed", RepoPull.Summary(results));
        Assert.True(RepoPull.IsNoteworthy(results));
        Assert.False(RepoPull.IsNoteworthy([R(RepoPullStatus.UpToDate)]));
        Assert.Equal("No repositories selected.", RepoPull.Summary([]));
    }

    [Fact]
    public void Excluded_repos_are_not_selected_ignoring_case_and_trailing_slash()
    {
        var sel = RepoPull.Selected([@"G:\Dev\A", @"G:\Dev\B", @"G:\Dev\C"], [@"g:\dev\b\"]);
        Assert.Equal(new[] { @"G:\Dev\A", @"G:\Dev\C" }, sel);
    }

    [Fact]
    public void Schedule_runs_once_at_startup_or_every_interval()
    {
        var start = new DateTime(2026, 11, 5, 9, 0, 0);
        var s = new RepoSettings { Root = @"G:\Dev", Auto = RepoAutoPull.Off };
        Assert.Null(RepoPull.NextDue(s, start));

        s.Auto = RepoAutoPull.AtStartup;
        s.LastRun = start.AddDays(-1);
        Assert.Equal(start, RepoPull.NextDue(s, start));
        s.LastRun = start.AddMinutes(1);
        Assert.Null(RepoPull.NextDue(s, start));

        s.Auto = RepoAutoPull.Interval;
        s.IntervalMinutes = 30;
        Assert.Equal(start.AddMinutes(31), RepoPull.NextDue(s, start));
        s.LastRun = null;
        Assert.Equal(start, RepoPull.NextDue(s, start));

        s.Root = "";
        Assert.Null(RepoPull.NextDue(s, start));
    }

    // ── Fake git: the safety rules ──────────────────────────────────────

    [Fact]
    public async Task The_result_keeps_what_git_printed()
    {
        var git = new FakeGit(a => a[0] == "status" ? new GitRun(0, "", "") : new GitRun(0, "Updating a..b\nFast-forward\n", "From origin\n"));
        var r = await new RepoPull(git).PullAsync(FakeRepo("out"));
        Assert.Contains("$ git pull --ff-only", r.Output);
        Assert.Contains("Fast-forward", r.Transcript);
        Assert.Contains("From origin", r.Transcript);
    }

    private sealed class FakeGit(Func<IReadOnlyList<string>, GitRun> answer) : IGitRunner
    {
        public readonly List<string> Calls = [];
        public Task<GitRun> RunAsync(string workingDirectory, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct = default)
        {
            Calls.Add(string.Join(' ', args));
            return Task.FromResult(answer(args));
        }
    }

    [Fact]
    public async Task Dirty_repo_is_skipped_without_pulling()
    {
        var repo = FakeRepo("dirty");
        var git = new FakeGit(a => a[0] == "status" ? new GitRun(0, " M a.txt\n", "") : new GitRun(0, "Already up to date.", ""));
        var r = await new RepoPull(git).PullAsync(repo);
        Assert.Equal(RepoPullStatus.LocalChanges, r.Status);
        Assert.Equal(new[] { "status --porcelain --untracked-files=no" }, git.Calls);
    }

    [Fact]
    public async Task Pull_is_fast_forward_only()
    {
        var repo = FakeRepo("clean");
        var git = new FakeGit(a => new GitRun(0, a[0] == "status" ? "" : "Already up to date.", ""));
        var r = await new RepoPull(git).PullAsync(repo);
        Assert.Equal(RepoPullStatus.UpToDate, r.Status);
        Assert.Equal("pull --ff-only", git.Calls[1]);
    }

    [Fact]
    public async Task Missing_git_stops_the_run()
    {
        var a = FakeRepo("a");
        var b = FakeRepo("b");
        var git = new FakeGit(_ => new GitRun(-1, "", "", GitMissing: true));
        var results = await new RepoPull(git).PullAllAsync([a, b]);
        Assert.Single(results);
        Assert.Equal(RepoPullStatus.GitMissing, results[0].Status);
    }

    [Fact]
    public async Task Folder_that_is_not_a_repo_is_reported_without_running_git()
    {
        var git = new FakeGit(_ => new GitRun(0, "", ""));
        var r = await new RepoPull(git).PullAsync(Dir("plain"));
        Assert.Equal(RepoPullStatus.NotARepo, r.Status);
        Assert.Empty(git.Calls);
    }

    // ── Real git (CI runner has Git for Windows) ────────────────────────

    private static readonly TimeSpan T = TimeSpan.FromSeconds(60);

    private static async Task Git(string dir, params string[] args)
    {
        var all = new List<string> { "-c", "user.name=DevPilot Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false", "-c", "init.defaultBranch=main" };
        all.AddRange(args);
        var r = await new GitRunner().RunAsync(dir, all, T);
        Assert.False(r.GitMissing, "git is not installed");
        Assert.True(r.ExitCode == 0, $"git {string.Join(' ', args)} failed: {r.StdErr}");
    }

    [Fact]
    public async Task Real_git_pull_updates_skips_and_refuses_to_merge()
    {
        var seed = Dir("seed");
        await Git(seed, "init");
        File.WriteAllText(Path.Combine(seed, "a.txt"), "one\n");
        await Git(seed, "add", "a.txt");
        await Git(seed, "commit", "-m", "first");
        await Git(_root, "clone", "--bare", seed, "origin.git");
        var origin = Path.Combine(_root, "origin.git");
        await Git(_root, "clone", origin, "a");
        await Git(_root, "clone", origin, "b");
        var a = Path.Combine(_root, "a");
        var b = Path.Combine(_root, "b");
        var pull = new RepoPull(new GitRunner());

        Assert.Equal(RepoPullStatus.UpToDate, (await pull.PullAsync(b)).Status);

        File.WriteAllText(Path.Combine(a, "a.txt"), "two\n");
        await Git(a, "commit", "-am", "second");
        await Git(a, "push");
        var updated = await pull.PullAsync(b);
        Assert.Equal(RepoPullStatus.Updated, updated.Status);
        Assert.Equal("two\n", File.ReadAllText(Path.Combine(b, "a.txt")).Replace("\r\n", "\n"));

        // Uncommitted change: skipped, file untouched
        File.WriteAllText(Path.Combine(b, "a.txt"), "local\n");
        Assert.Equal(RepoPullStatus.LocalChanges, (await pull.PullAsync(b)).Status);
        Assert.Equal("local\n", File.ReadAllText(Path.Combine(b, "a.txt")));
        await Git(b, "checkout", "--", "a.txt");

        // Local commit + new remote commit: no merge
        File.WriteAllText(Path.Combine(b, "b.txt"), "b\n");
        await Git(b, "add", "b.txt");
        await Git(b, "commit", "-m", "local");
        File.WriteAllText(Path.Combine(a, "a.txt"), "three\n");
        await Git(a, "commit", "-am", "third");
        await Git(a, "push");
        var diverged = await pull.PullAsync(b);
        Assert.Equal(RepoPullStatus.Diverged, diverged.Status);
        Assert.Equal("two\n", File.ReadAllText(Path.Combine(b, "a.txt")).Replace("\r\n", "\n"));

        // Branch without upstream
        await Git(a, "checkout", "-b", "solo");
        Assert.Equal(RepoPullStatus.NoUpstream, (await pull.PullAsync(a)).Status);
        Assert.Equal("solo", RepoScanner.ReadBranch(a));
    }
}
