using DevPilot.Core.Interfaces;
using DevPilot.Core.Repos;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

/// <summary>A repository with a cloud copy (synced drive) and a local copy the application runs from.</summary>
public sealed class RepoCopiesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "devpilot-copies-" + Guid.NewGuid().ToString("N"));

    public RepoCopiesTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private string Repo(string name, params string[] files)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        foreach (var f in files)
        {
            var p = Path.Combine(dir, f);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "");
        }
        return dir;
    }

    private static RepoPullResult R(RepoPullStatus s, string m = "") => new("x", s, m, DateTime.Now);

    // ── Which copies ────────────────────────────────────────────────────

    [Fact]
    public void Without_a_local_path_the_found_clone_is_the_only_copy_and_installs()
    {
        var repo = Repo("one");
        var copies = RepoCopies.All(repo, null);
        Assert.Single(copies);
        Assert.Equal(RepoCopyKind.Cloud, copies[0].Kind);
        Assert.True(copies[0].Installs);
        Assert.Equal(RepoScanner.Normalize(repo), RepoCopies.InstallPath(repo, null));
    }

    [Fact]
    public void With_a_local_path_the_cloud_copy_is_pulled_first_and_the_local_copy_installs()
    {
        var repo = Repo("two");
        var local = Path.Combine(_root, "apps", "two");
        var cfg = new RepoConfig { Path = repo, LocalPath = local };
        var copies = RepoCopies.All(repo, cfg);
        Assert.Equal(2, copies.Count);
        Assert.False(copies[0].Installs);
        Assert.Equal(RepoCopyKind.Local, copies[1].Kind);
        Assert.True(copies[1].Installs);
        Assert.Equal(RepoScanner.Normalize(local), RepoCopies.InstallPath(repo, cfg));
    }

    [Fact]
    public void A_local_path_equal_to_the_cloud_copy_counts_once_and_a_cloud_path_overrides_the_found_clone()
    {
        var repo = Repo("same");
        Assert.Single(RepoCopies.All(repo, new RepoConfig { Path = repo, LocalPath = repo + Path.DirectorySeparatorChar }));
        var drive = Path.Combine(_root, "drive", "same");
        Assert.Equal(RepoScanner.Normalize(drive), RepoCopies.CloudPath(repo, new RepoConfig { Path = repo, CloudPath = drive }));
    }

    [Fact]
    public void Clone_only_into_a_missing_or_empty_folder()
    {
        Assert.True(RepoCopies.CanClone(Path.Combine(_root, "missing")));
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        Assert.True(RepoCopies.CanClone(empty));
        var full = Path.Combine(_root, "full");
        Directory.CreateDirectory(full);
        File.WriteAllText(Path.Combine(full, "notes.txt"), "keep");
        Assert.False(RepoCopies.CanClone(full));
        Assert.False(RepoCopies.NeedsClone(Repo("cloned")));
    }

    [Fact]
    public void Normalize_keeps_an_entry_that_only_sets_a_local_path()
    {
        var s = new RepoSettings();
        s.Items.Add(new RepoConfig { Path = Path.Combine(_root, "a"), LocalPath = "  \"" + Path.Combine(_root, "local", "a") + "\\\"  " });
        RepoConfigs.Normalize(s);
        Assert.Single(s.Items);
        Assert.Equal(RepoScanner.Normalize(Path.Combine(_root, "local", "a")), s.Items[0].LocalPath);
    }

    // ── One result for both copies ──────────────────────────────────────

    [Fact]
    public void Merge_keeps_the_worst_status_and_names_each_copy()
    {
        var cloud = new RepoCopy(RepoCopyKind.Cloud, "c", false);
        var local = new RepoCopy(RepoCopyKind.Local, "l", true);
        var m = RepoCopies.Merge("repo", [(cloud, R(RepoPullStatus.LocalChanges, "2 files")), (local, R(RepoPullStatus.Updated, "1 file changed"))]);
        Assert.Equal(RepoPullStatus.LocalChanges, m.Status);
        Assert.Equal("repo", m.Path);
        Assert.Contains("cloud: skipped: local changes", m.Message);
        Assert.Contains("local: 1 file changed", m.Message);

        var failed = RepoCopies.Merge("repo", [(cloud, R(RepoPullStatus.UpToDate)), (local, R(RepoPullStatus.Offline, "no network"))]);
        Assert.Equal(RepoPullStatus.Offline, failed.Status);
        var ok = RepoCopies.Merge("repo", [(cloud, R(RepoPullStatus.UpToDate)), (local, R(RepoPullStatus.Updated))]);
        Assert.Equal(RepoPullStatus.Updated, ok.Status);
    }

    // ── Pull and Update on both copies ──────────────────────────────────

    private sealed class FakeConfig(AppSettings s) : IConfigStore
    {
        public AppSettings Current { get; } = s;
        public string ConfigPath => string.Empty;
        public string DataFolder => string.Empty;
        public string? LoadWarning => null;
        public event EventHandler<ConfigArea>? Changed;
        public void Load() { }
        public void Save() { }
        public void Touch(ConfigArea area) => Changed?.Invoke(this, area);
        public void Export(string path) => throw new NotSupportedException();
        public void Import(string path) => throw new NotSupportedException();
        public void Reset() => throw new NotSupportedException();
        public void Replace(AppSettings settings) => throw new NotSupportedException();
    }

    private sealed class FakeGit(string pullOut) : IGitRunner
    {
        public readonly List<string> Calls = [];
        public Task<GitRun> RunAsync(string workingDirectory, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct = default)
        {
            lock (Calls) Calls.Add($"{Path.GetFileName(workingDirectory.TrimEnd('\\', '/'))}: git {args[0]}");
            return Task.FromResult(args[0] switch
            {
                "status" => new GitRun(0, "", ""),
                "remote" => new GitRun(0, "https://example.invalid/app.git\n", ""),
                "clone" => new GitRun(0, "", "Cloning into 'app'...\n"),
                _ => new GitRun(0, pullOut, ""),
            });
        }
    }

    private sealed class FakeCommands : ICommandRunner
    {
        public readonly List<string> Dirs = [];
        public Task<CommandRun> RunAsync(RepoCommand command, string workingDirectory, TimeSpan timeout, CancellationToken ct = default)
        {
            Dirs.Add(workingDirectory);
            return Task.FromResult(new CommandRun(0, "installed\n"));
        }
    }

    private sealed class FakeTools(params string[] running) : IToolControl
    {
        public readonly List<string> Calls = [];
        private readonly HashSet<string> _running = [.. running];
        public bool IsRunning(ToolEntry tool) => _running.Contains(tool.Id);
        public Task StopAsync(ToolEntry tool) { Calls.Add("stop " + tool.Name); _running.Remove(tool.Id); return Task.CompletedTask; }
        public Task<LaunchResult> StartAsync(ToolEntry tool) { Calls.Add("start " + tool.Name); return Task.FromResult(LaunchResult.Ok()); }
    }

    private const string Pulled = "Updating a..b\nFast-forward\n x.py | 2 +-\n 1 file changed, 1 insertion(+), 1 deletion(-)\n";

    [Fact]
    public async Task Update_pulls_both_copies_and_installs_and_restarts_only_from_the_local_copy()
    {
        var cloud = Repo("cloud", "tool.py", "Install.bat");
        var local = Repo(Path.Combine("apps", "local"), "tool.py", "Install.bat");
        var settings = new AppSettings();
        settings.Repos.Items.Add(new RepoConfig { Path = cloud, LocalPath = local });
        settings.Tools.Add(new ToolEntry { Id = "L", Name = "FromLocal", Kind = ToolKind.Python, Path = Path.Combine(local, "tool.py") });
        settings.Tools.Add(new ToolEntry { Id = "C", Name = "FromCloud", Kind = ToolKind.Python, Path = Path.Combine(cloud, "tool.py") });
        var git = new FakeGit(Pulled);
        var commands = new FakeCommands();
        var tools = new FakeTools("L", "C");
        var svc = new RepoSyncService(new FakeConfig(settings), git, commands, tools);

        var r = await svc.UpdateAsync(cloud);

        Assert.Equal(RepoCommandStatus.Succeeded, r.Status);
        Assert.Equal(new[] { "cloud: git status", "cloud: git pull", "local: git status", "local: git pull" }, git.Calls);
        Assert.Equal(new[] { RepoScanner.Normalize(local) }, commands.Dirs);
        Assert.Equal(new[] { "stop FromLocal", "start FromLocal" }, tools.Calls);
        Assert.Contains("Pulled the cloud copy", r.OutputTail);
    }

    [Fact]
    public async Task Pull_clones_a_local_copy_that_does_not_exist_yet()
    {
        var cloud = Repo("app");
        var local = Path.Combine(_root, "apps", "app");
        var settings = new AppSettings();
        settings.Repos.Items.Add(new RepoConfig { Path = cloud, LocalPath = local });
        var git = new FakeGit("Already up to date.\n");
        var svc = new RepoSyncService(new FakeConfig(settings), git, new FakeCommands(), new FakeTools());

        var r = await svc.PullOneAsync(cloud);

        Assert.NotNull(r);
        Assert.Equal(RepoPullStatus.Updated, r!.Status);
        Assert.Contains("app: git remote", git.Calls);
        Assert.Contains("apps: git clone", git.Calls);   // runs in the parent folder
        Assert.Contains("local: Cloned", r.Message);
        Assert.True(Directory.Exists(Path.Combine(_root, "apps")));
    }

    [Fact]
    public async Task Pull_of_the_local_copy_only_leaves_the_cloud_copy_alone()
    {
        var cloud = Repo("c2");
        var local = Repo("l2");
        var settings = new AppSettings();
        settings.Repos.Items.Add(new RepoConfig { Path = cloud, LocalPath = local });
        var git = new FakeGit("Already up to date.\n");
        var svc = new RepoSyncService(new FakeConfig(settings), git, new FakeCommands(), new FakeTools());

        var r = await svc.PullOneAsync(cloud, RepoCopyKind.Local);

        Assert.Equal(RepoPullStatus.UpToDate, r!.Status);
        Assert.All(git.Calls, c => Assert.StartsWith("l2:", c));
        Assert.StartsWith("local:", r.Message);
    }
}
