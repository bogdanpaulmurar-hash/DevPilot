using DevPilot.Core.Interfaces;
using DevPilot.Core.Repos;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public sealed class RepoUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "devpilot-upd-" + Guid.NewGuid().ToString("N"));

    public RepoUpdateTests() => Directory.CreateDirectory(_root);
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

    private static ToolEntry Tool(string id, string name, string path = "", string workingDirectory = "", ToolKind kind = ToolKind.Python) =>
        new() { Id = id, Name = name, Kind = kind, Path = path, WorkingDirectory = workingDirectory };

    // ── Detection ───────────────────────────────────────────────────────

    [Fact]
    public void Kinds_are_detected_from_the_files()
    {
        Assert.Equal(RepoProjectKind.CSharp, RepoUpdatePlanner.Detect(Repo("cs1", "App.sln")));
        Assert.Equal(RepoProjectKind.CSharp, RepoUpdatePlanner.Detect(Repo("cs2", @"src\App\App.csproj")));
        Assert.Equal(RepoProjectKind.CSharp, RepoUpdatePlanner.Detect(Repo("mixed", "App.sln", "tools.py")));
        Assert.Equal(RepoProjectKind.Python, RepoUpdatePlanner.Detect(Repo("py1", "FocusPilot.pyw")));
        Assert.Equal(RepoProjectKind.Python, RepoUpdatePlanner.Detect(Repo("py2", "pyproject.toml")));
        Assert.Equal(RepoProjectKind.Python, RepoUpdatePlanner.Detect(Repo("py3", @"src\tool\__main__.py")));
        Assert.Equal(RepoProjectKind.Unknown, RepoUpdatePlanner.Detect(Repo("docs", "README.md")));
        Assert.Equal(RepoProjectKind.Unknown, RepoUpdatePlanner.Detect(Repo("buried", @"obj\x\Y.csproj")));   // build folders are ignored
    }

    [Fact]
    public void Python_uses_its_install_script()
    {
        var repo = Repo("py", "tool.pyw", "Install.bat");
        var cmd = RepoUpdatePlanner.DetectInstall(repo, RepoProjectKind.Python, out var text)!;
        Assert.Equal("Install.bat", text);
        Assert.Equal(Path.Combine(repo, "Install.bat"), cmd.CommandLine);
        Assert.Equal(RepoCommandMode.Hidden, cmd.Mode);
    }

    [Fact]
    public void Install_script_in_scripts_folder_and_powershell_scripts()
    {
        var repo = Repo("scripted", "App.sln", @"scripts\install.cmd");
        var cmd = RepoUpdatePlanner.DetectInstall(repo, RepoProjectKind.CSharp, out var text)!;
        Assert.Equal(@"scripts\install.cmd", text);
        Assert.Equal(Path.Combine(repo, "scripts", "install.cmd"), cmd.CommandLine);

        var ps = Repo("ps", "tool.py", @"scripts\install.ps1");
        var psCmd = RepoUpdatePlanner.DetectInstall(ps, RepoProjectKind.Python, out _)!;
        Assert.StartsWith("powershell -NoProfile -ExecutionPolicy Bypass -File ", psCmd.CommandLine);
        Assert.EndsWith("install.ps1", psCmd.CommandLine);
    }

    [Fact]
    public void CSharp_without_a_script_builds_in_release()
    {
        var repo = Repo("cs", "My App.sln", @"src\My App\My App.csproj");
        var cmd = RepoUpdatePlanner.DetectInstall(repo, RepoProjectKind.CSharp, out var text)!;
        Assert.Equal("dotnet build My App.sln -c Release", text);
        Assert.Equal($"dotnet build \"{Path.Combine(repo, "My App.sln")}\" -c Release -nologo -v:minimal", cmd.CommandLine);

        var projOnly = Repo("cs-proj", @"Deep\Deep.csproj", @"Other\Nested\Other.csproj");
        Assert.Equal(Path.Combine(projOnly, "Deep", "Deep.csproj"), RepoUpdatePlanner.FindBuildTarget(projOnly));
    }

    [Fact]
    public void Python_without_a_script_installs_requirements_into_its_venv_only()
    {
        var plain = Repo("py-plain", "tool.py", "requirements.txt");
        Assert.Null(RepoUpdatePlanner.DetectInstall(plain, RepoProjectKind.Python, out var none));
        Assert.Equal("", none);

        var venv = Repo("py-venv", "tool.py", "requirements.txt", @".venv\Scripts\python.exe");
        var cmd = RepoUpdatePlanner.DetectInstall(venv, RepoProjectKind.Python, out var text)!;
        Assert.Contains("-m pip install -r requirements.txt", cmd.CommandLine);
        Assert.StartsWith(Path.Combine(venv, ".venv", "Scripts", "python.exe"), cmd.CommandLine);
        Assert.Contains(".venv", text);
    }

    // ── Plan ────────────────────────────────────────────────────────────

    [Fact]
    public void Tools_that_start_from_inside_the_repository_are_found()
    {
        var repo = Repo("sp", "tool.py");
        var other = Repo("other", "x.py");
        var tools = new[]
        {
            Tool("1", "By path", Path.Combine(repo, "tool.py")),
            Tool("2", "By folder", workingDirectory: Path.Combine(repo, "src")),
            Tool("3", "Elsewhere", Path.Combine(other, "x.py")),
            Tool("4", "Sibling prefix", Path.Combine(_root, "sp-old", "x.py")),
            Tool("5", "Folder entry", repo, kind: ToolKind.Folder),
            Tool("6", "Exe", Path.Combine(repo, "bin", "App.exe"), kind: ToolKind.Executable),
        };
        var found = RepoUpdatePlanner.ToolsInRepo(repo, tools);
        Assert.Equal(["By path", "By folder", "Exe"], found.Select(t => t.Name));
    }

    [Fact]
    public void Plan_is_available_with_an_install_step_or_a_tool_and_off_when_switched_off()
    {
        var withInstall = Repo("a", "t.py", "install.bat");
        Assert.True(RepoUpdatePlanner.Plan(withInstall, null, []).Available);

        var toolOnly = Repo("b", "t.py");
        Assert.False(RepoUpdatePlanner.Plan(toolOnly, null, []).Available);
        var plan = RepoUpdatePlanner.Plan(toolOnly, null, [Tool("1", "T", Path.Combine(toolOnly, "t.py"))]);
        Assert.True(plan.Available);
        Assert.Null(plan.Install);
        Assert.Contains("no install step found", plan.Summary);
        Assert.Contains("closes and restarts: T", plan.Summary);

        var off = RepoUpdatePlanner.Plan(withInstall, new RepoConfig { UpdateEnabled = false }, []);
        Assert.False(off.Available);
        Assert.Contains("switched off", off.Note);
    }

    [Fact]
    public void Install_command_can_be_overridden()
    {
        var repo = Repo("c", "App.sln", "install.bat");
        var plan = RepoUpdatePlanner.Plan(repo, new RepoConfig { InstallCommand = "  dotnet publish -c Release  " }, []);
        Assert.Equal("dotnet publish -c Release", plan.Install!.CommandLine);
        Assert.Equal("dotnet publish -c Release", plan.InstallText);
    }

    [Fact]
    public void DevPilot_itself_is_never_updated_from_inside()
    {
        var repo = Repo("DevPilot", "DevPilot.sln", @"scripts\install.cmd");
        var plan = RepoUpdatePlanner.Plan(repo, null, []);
        Assert.False(plan.Available);
        Assert.Contains("DevPilot updates itself", plan.Note);
    }

    // ── Actions with a plan ─────────────────────────────────────────────

    [Fact]
    public void Update_replaces_the_plain_pull_unless_pull_is_always_on()
    {
        var s = new RepoSettings();
        var repo = Repo("x", "t.py", "install.bat");
        var plan = RepoUpdatePlanner.Plan(repo, null, []);
        Assert.Equal(["Update"], RepoConfigs.Actions(s, repo, plan).Select(a => a.Name));

        var cfg = RepoConfigs.GetOrCreate(s, repo);
        cfg.Commands.Add(new RepoCommand { Name = "Test", CommandLine = "pytest" });
        Assert.Equal(["Update", "Test"], RepoConfigs.Actions(s, repo, plan).Select(a => a.Name));

        cfg.Pull = PullButton.Always;
        var all = RepoConfigs.Actions(s, repo, plan);
        Assert.Equal(["Update", "git pull", "Test"], all.Select(a => a.Name));
        Assert.True(all[0].IsUpdate);
        Assert.True(all[1].IsPull);

        cfg.Pull = PullButton.Never;
        Assert.Equal(["Update", "Test"], RepoConfigs.Actions(s, repo, plan).Select(a => a.Name));

        // No update available: the plain pull comes back (Auto)
        cfg.Pull = PullButton.Auto;
        Assert.Equal(["git pull", "Test"], RepoConfigs.Actions(s, repo, RepoUpdatePlan.None()).Select(a => a.Name));
    }

    [Fact]
    public void Normalize_keeps_entries_with_update_settings()
    {
        var s = new RepoSettings();
        s.Items.Add(new RepoConfig { Path = @"G:\Dev\A", InstallCommand = " x.bat " });
        s.Items.Add(new RepoConfig { Path = @"G:\Dev\B", RestartTools = false });
        s.Items.Add(new RepoConfig { Path = @"G:\Dev\C", UpdateEnabled = false });
        s.Items.Add(new RepoConfig { Path = @"G:\Dev\D", Pull = PullButton.Never });
        s.Items.Add(new RepoConfig { Path = @"G:\Dev\E" });
        RepoConfigs.Normalize(s);
        Assert.Equal(4, s.Items.Count);
        Assert.Equal("x.bat", s.Items[0].InstallCommand);
    }

    // ── Update flow with fakes ──────────────────────────────────────────

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

    private sealed class Log
    {
        public readonly List<string> Entries = [];
        public void Add(string e) { lock (Entries) Entries.Add(e); }
        public string Joined { get { lock (Entries) return string.Join(" | ", Entries); } }
    }

    private sealed class FakeGit(Log log, string status, string pullOut) : IGitRunner
    {
        public Task<GitRun> RunAsync(string workingDirectory, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct = default)
        {
            log.Add("git " + args[0]);
            return Task.FromResult(args[0] == "status" ? new GitRun(0, status, "") : new GitRun(0, pullOut, ""));
        }
    }

    private sealed class FakeCommands(Log log, int exit = 0, string output = "built ok\n") : ICommandRunner
    {
        public Task<CommandRun> RunAsync(RepoCommand command, string workingDirectory, TimeSpan timeout, CancellationToken ct = default)
        {
            log.Add("run " + command.CommandLine);
            return Task.FromResult(new CommandRun(exit, output));
        }
    }

    private sealed class FakeTools(Log log, params string[] running) : IToolControl
    {
        private readonly HashSet<string> _running = [.. running];
        public bool FailStart;
        public bool IsRunning(ToolEntry tool) => _running.Contains(tool.Id);
        public Task StopAsync(ToolEntry tool) { log.Add("stop " + tool.Name); _running.Remove(tool.Id); return Task.CompletedTask; }
        public Task<LaunchResult> StartAsync(ToolEntry tool)
        {
            log.Add("start " + tool.Name);
            return Task.FromResult(FailStart ? LaunchResult.Fail("exe missing") : LaunchResult.Ok());
        }
    }

    private const string Pulled = "Updating a..b\nFast-forward\n x.py | 2 +-\n 1 file changed, 1 insertion(+), 1 deletion(-)\n";

    private (RepoSyncService Svc, Log Log, string Repo, FakeTools Tools) Setup(string status = "", string pullOut = Pulled, int installExit = 0,
        string[]? running = null, bool twoTools = false)
    {
        var repo = Repo("tool", "tool.py", "Install.bat");
        var log = new Log();
        var settings = new AppSettings();
        settings.Tools.Add(Tool("1", "ScreenPilot", Path.Combine(repo, "tool.py")));
        if (twoTools) settings.Tools.Add(Tool("2", "Helper", workingDirectory: repo));
        settings.Tools.Add(Tool("9", "Unrelated", Path.Combine(_root, "elsewhere", "x.py")));
        var tools = new FakeTools(log, running ?? ["1"]);
        var svc = new RepoSyncService(new FakeConfig(settings), new FakeGit(log, status, pullOut), new FakeCommands(log, installExit, installExit == 0 ? "built ok\n" : "error: missing module\n"), tools);
        return (svc, log, repo, tools);
    }

    [Fact]
    public async Task Update_pulls_closes_the_running_tool_installs_and_starts_it_again_in_that_order()
    {
        var (svc, log, repo, _) = Setup();
        var r = await svc.UpdateAsync(repo);
        Assert.Equal(RepoCommandStatus.Succeeded, r.Status);
        Assert.True(r.ShowAllLines);
        var install = Path.Combine(repo, "Install.bat");
        Assert.Equal($"git status | git pull | stop ScreenPilot | run {install} | start ScreenPilot", log.Joined);
        Assert.Contains("Closed: ScreenPilot", r.OutputTail);
        Assert.Contains("Installed: Install.bat", r.OutputTail);
        Assert.Contains("Started: ScreenPilot", r.OutputTail);
    }

    [Fact]
    public async Task Update_with_nothing_new_closes_and_installs_nothing()
    {
        var (svc, log, repo, _) = Setup(pullOut: "Already up to date.\n");
        var r = await svc.UpdateAsync(repo);
        Assert.Equal(RepoCommandStatus.Succeeded, r.Status);
        Assert.Equal("git status | git pull", log.Joined);
        Assert.Contains("Already up to date", r.OutputTail);
    }

    [Fact]
    public async Task Update_is_skipped_for_uncommitted_changes_and_closes_nothing()
    {
        var (svc, log, repo, _) = Setup(status: " M tool.py\n");
        var r = await svc.UpdateAsync(repo);
        Assert.Equal(RepoCommandStatus.Skipped, r.Status);
        Assert.Equal("git status", log.Joined);
        Assert.Contains("local changes", r.OutputTail);
    }

    [Fact]
    public async Task Failed_install_keeps_the_tool_closed_and_says_so()
    {
        var (svc, log, repo, _) = Setup(installExit: 5);
        var r = await svc.UpdateAsync(repo);
        Assert.Equal(RepoCommandStatus.Failed, r.Status);
        Assert.Equal(5, r.ExitCode);
        Assert.DoesNotContain("start ", log.Joined);
        Assert.Contains("error: missing module", r.OutputTail);
        Assert.Contains("ScreenPilot stays closed", r.OutputTail);
    }

    [Fact]
    public async Task Only_tools_that_were_running_are_closed_and_started_again()
    {
        var (svc, log, repo, _) = Setup(running: ["2"], twoTools: true);
        await svc.UpdateAsync(repo);
        Assert.Contains("stop Helper", log.Joined);
        Assert.Contains("start Helper", log.Joined);
        Assert.DoesNotContain("ScreenPilot", log.Joined);
        Assert.DoesNotContain("Unrelated", log.Joined);

        var (svc2, log2, repo2, _) = Setup(running: []);
        await svc2.UpdateAsync(repo2);
        Assert.DoesNotContain("stop", log2.Joined);
        Assert.DoesNotContain("start", log2.Joined);
        Assert.Contains("run ", log2.Joined);                 // still installed
    }

    [Fact]
    public async Task Restart_off_leaves_running_tools_alone()
    {
        var (svc, log, repo, _) = Setup();
        RepoConfigs.GetOrCreate(svc.Settings, repo).RestartTools = false;
        await svc.UpdateAsync(repo);
        Assert.DoesNotContain("stop", log.Joined);
        Assert.DoesNotContain("start", log.Joined);
        Assert.Contains("run ", log.Joined);
    }

    [Fact]
    public async Task Tool_that_does_not_start_again_is_reported()
    {
        var (svc, _, repo, tools) = Setup();
        tools.FailStart = true;
        var r = await svc.UpdateAsync(repo);
        Assert.Equal(RepoCommandStatus.Failed, r.Status);
        Assert.Contains("Did not start ScreenPilot: exe missing", r.OutputTail);
    }

    [Fact]
    public async Task Reinstall_does_not_pull()
    {
        var (svc, log, repo, _) = Setup();
        var r = await svc.UpdateAsync(repo, RepoUpdateMode.ReinstallOnly);
        Assert.Equal("Reinstall", r.Name);
        Assert.Equal(RepoCommandStatus.Succeeded, r.Status);
        Assert.DoesNotContain("git", log.Joined);
        Assert.StartsWith("stop ScreenPilot | run ", log.Joined);
    }

    [Fact]
    public async Task Repository_without_install_step_or_tool_has_nothing_to_update()
    {
        var repo = Repo("docs", "README.md");
        var svc = new RepoSyncService(new FakeConfig(new AppSettings()), new FakeGit(new Log(), "", Pulled), new FakeCommands(new Log()), new FakeTools(new Log()));
        var r = await svc.UpdateAsync(repo);
        Assert.Equal(RepoCommandStatus.Skipped, r.Status);
    }

    [Fact]
    public async Task Last_command_result_is_kept_for_the_row()
    {
        var (svc, _, repo, _) = Setup(pullOut: "Already up to date.\n");
        await svc.UpdateAsync(repo);
        Assert.Equal("Update", svc.LastCommandResult(repo)!.Name);
    }
}
