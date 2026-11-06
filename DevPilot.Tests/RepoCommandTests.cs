using DevPilot.Core.Config;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Repos;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public sealed class RepoCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "devpilot-cmd-" + Guid.NewGuid().ToString("N"));

    public RepoCommandTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static RepoCommand Cmd(string name, string line, RepoCommandMode mode = RepoCommandMode.Hidden) =>
        new() { Name = name, CommandLine = line, Mode = mode };

    // ── Actions per repository ──────────────────────────────────────────

    [Fact]
    public void A_failed_hidden_command_that_needed_a_sign_in_says_so()
    {
        var r = RepoCommandRunner.ToResult(@"G:\Dev\A", "Git Pull", new CommandRun(1, "fatal: could not read Password for 'https://x@github.com': terminal prompts disabled\n"), DateTime.Now);
        Assert.Equal(RepoCommandStatus.Failed, r.Status);
        Assert.Contains("In a window", r.OutputTail);
        var ok = RepoCommandRunner.ToResult(@"G:\Dev\A", "Build", new CommandRun(1, "error CS1002\n"), DateTime.Now);
        Assert.DoesNotContain("In a window", ok.OutputTail);
    }

    [Fact]
    public void Repo_without_settings_has_only_git_pull()
    {
        var actions = RepoConfigs.Actions(new RepoSettings(), @"G:\Dev\A");
        Assert.Single(actions);
        Assert.True(actions[0].IsPull);
    }

    [Fact]
    public void Pull_comes_first_then_the_commands_and_can_be_switched_off()
    {
        var s = new RepoSettings();
        var cfg = RepoConfigs.GetOrCreate(s, @"G:\Dev\A");
        cfg.Commands.Add(Cmd("Build", "dotnet build"));
        cfg.Commands.Add(Cmd("", "Install.bat"));
        var actions = RepoConfigs.Actions(s, @"g:\dev\a\");
        Assert.Equal(["git pull", "Build", "Install.bat"], actions.Select(a => a.Name));
        Assert.True(actions[0].IsPull);
        Assert.False(actions[1].IsPull);

        cfg.Pull = PullButton.Never;
        Assert.Equal(["Build", "Install.bat"], RepoConfigs.Actions(s, @"G:\Dev\A").Select(a => a.Name));
    }

    [Fact]
    public void Get_or_create_reuses_the_entry_for_the_same_folder()
    {
        var s = new RepoSettings();
        var a = RepoConfigs.GetOrCreate(s, @"G:\Dev\A");
        var b = RepoConfigs.GetOrCreate(s, @"g:\dev\a\");
        Assert.Same(a, b);
        Assert.Single(s.Items);
    }

    [Fact]
    public void Display_name_falls_back_to_the_program()
    {
        Assert.Equal("Install.bat", RepoConfigs.DisplayName(Cmd("", @"""C:\x\Install.bat"" /quiet")));
        Assert.Equal("dotnet", RepoConfigs.DisplayName(Cmd("", "dotnet build")));
        Assert.Equal("Build all", RepoConfigs.DisplayName(Cmd("  Build all ", "x")));
    }

    [Fact]
    public void Normalize_drops_empty_commands_default_entries_and_duplicate_ids()
    {
        var s = new RepoSettings();
        s.Items.Add(new RepoConfig { Path = @"G:\Dev\A", Commands = [Cmd("empty", "  "), Cmd(" Build ", " dotnet build ")] });
        s.Items[0].Commands.Add(new RepoCommand { Id = s.Items[0].Commands[1].Id, Name = "Test", CommandLine = "dotnet test", Mode = (RepoCommandMode)99 });
        s.Items.Add(new RepoConfig { Path = @"G:\Dev\B" });                      // only defaults: dropped
        s.Items.Add(new RepoConfig { Path = "", Commands = [Cmd("x", "y")] });     // no path: dropped
        s.Items.Add(new RepoConfig { Path = @"g:\dev\a\", Commands = [Cmd("dup", "dup")] }); // same folder as the first: dropped
        RepoConfigs.Normalize(s);
        var item = Assert.Single(s.Items);
        Assert.Equal(["Build", "Test"], item.Commands.Select(c => c.Name));
        Assert.Equal("dotnet build", item.Commands[0].CommandLine);
        Assert.NotEqual(item.Commands[0].Id, item.Commands[1].Id);
        Assert.Equal(RepoCommandMode.Hidden, item.Commands[1].Mode);
    }

    [Fact]
    public void Config_normalizer_repairs_repo_settings_and_survives_a_json_round_trip()
    {
        var s = new AppSettings();
        RepoConfigs.GetOrCreate(s.Repos, @"G:\Dev\A").Commands.Add(Cmd("Build", "dotnet build", RepoCommandMode.Window));
        var json = ConfigSerializer.Serialize(s);
        var back = ConfigSerializer.Load(json, out _);
        var c = Assert.Single(Assert.Single(back.Repos.Items).Commands);
        Assert.Equal(RepoCommandMode.Window, c.Mode);
        Assert.Equal("dotnet build", c.CommandLine);
    }

    // ── Arguments and results ───────────────────────────────────────────

    [Fact]
    public void Hidden_runs_and_exits_window_stays_open()
    {
        Assert.Equal("/d /s /c \"dotnet build\"", RepoCommandRunner.Arguments(Cmd("b", " dotnet build ")));
        Assert.Equal("/d /s /k \"\"C:\\x y\\Install.bat\" /q\"", RepoCommandRunner.Arguments(Cmd("i", "\"C:\\x y\\Install.bat\" /q", RepoCommandMode.Window)));
    }

    [Fact]
    public void Output_keeps_the_end_and_the_last_line_is_the_message()
    {
        var long_ = string.Join("\n", Enumerable.Range(1, 3000).Select(i => "line " + i));
        var tail = RepoCommandRunner.Tail(long_, 200);
        Assert.True(tail.Length <= 200);
        Assert.EndsWith("line 3000", tail);
        Assert.StartsWith("line ", tail);                       // cut at a line start
        Assert.Equal("", RepoCommandRunner.Tail(null));
        Assert.Equal("b\nc", RepoCommandRunner.LastLines("a\r\n\r\nb\r\nc\r\n", 2));
    }

    [Fact]
    public void Run_outcomes_map_to_statuses()
    {
        var at = DateTime.Now;
        RepoCommandResult R(CommandRun run) => RepoCommandRunner.ToResult(@"G:\Dev\A", "Build", run, at);
        Assert.Equal(RepoCommandStatus.Succeeded, R(new CommandRun(0, "ok\n")).Status);
        var failed = R(new CommandRun(2, "compiling\nerror CS1002\n"));
        Assert.Equal(RepoCommandStatus.Failed, failed.Status);
        Assert.Equal("error CS1002", failed.Message);
        Assert.Equal("Failed (exit code 2)", failed.StatusText);
        Assert.Equal(RepoCommandStatus.TimedOut, R(new CommandRun(-1, "x", TimedOut: true)).Status);
        Assert.Equal(RepoCommandStatus.Started, R(new CommandRun(0, "", Started: true)).Status);
        Assert.True(R(new CommandRun(0, "", Started: true)).IsOk);
        var noStart = R(new CommandRun(-1, "", StartError: "no cmd"));
        Assert.Equal(RepoCommandStatus.StartFailed, noStart.Status);
        Assert.Equal("no cmd", noStart.Message);
        Assert.Equal("1 done · 1 failed", RepoCommandRunner.Summary([R(new CommandRun(0, "")), failed]));
    }

    // ── Service: busy handling (fakes) ──────────────────────────────────

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

    private sealed class FakeGit : IGitRunner
    {
        public readonly List<string> Dirs = [];
        public Task<GitRun> RunAsync(string workingDirectory, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct = default)
        {
            Dirs.Add(workingDirectory);
            return Task.FromResult(new GitRun(0, args[0] == "status" ? "" : "Already up to date.", ""));
        }
    }

    private sealed class FakeTools : IToolControl
    {
        public readonly HashSet<string> Running = [];
        public readonly List<string> Calls = [];
        public bool IsRunning(ToolEntry tool) => Running.Contains(tool.Id);
        public Task StopAsync(ToolEntry tool) { Calls.Add("stop " + tool.Name); Running.Remove(tool.Id); return Task.CompletedTask; }
        public Task<LaunchResult> StartAsync(ToolEntry tool) { Calls.Add("start " + tool.Name); Running.Add(tool.Id); return Task.FromResult(LaunchResult.Ok()); }
    }

    private sealed class SlowCommands(TaskCompletionSource release) : ICommandRunner
    {
        public readonly TaskCompletionSource Started = new();
        public async Task<CommandRun> RunAsync(RepoCommand command, string workingDirectory, TimeSpan timeout, CancellationToken ct = default)
        {
            Started.TrySetResult();
            await release.Task;
            return new CommandRun(0, "built\n");
        }
    }

    [Fact]
    public async Task A_pull_skips_a_repository_that_is_running_a_command_and_a_second_command_is_refused()
    {
        var a = Path.Combine(_root, "a");
        var b = Path.Combine(_root, "b");
        foreach (var d in new[] { a, b }) Directory.CreateDirectory(Path.Combine(d, ".git"));
        var settings = new AppSettings();
        settings.Repos.Root = _root;
        var release = new TaskCompletionSource();
        var commands = new SlowCommands(release);
        var git = new FakeGit();
        var svc = new RepoSyncService(new FakeConfig(settings), git, commands, new FakeTools());

        var running = svc.RunCommandAsync(a, Cmd("Build", "dotnet build"));
        await commands.Started.Task;
        Assert.True(svc.IsBusy(a));

        var second = await svc.RunCommandAsync(a, Cmd("Again", "x"));
        Assert.Equal(RepoCommandStatus.Busy, second.Status);

        var pulled = await svc.PullSelectedAsync();
        Assert.NotNull(pulled);
        Assert.Equal(RepoPullStatus.Busy, pulled!.Single(r => RepoScanner.SamePath(r.Path, a)).Status);
        Assert.Equal(RepoPullStatus.UpToDate, pulled!.Single(r => RepoScanner.SamePath(r.Path, b)).Status);
        Assert.DoesNotContain(git.Dirs, d => RepoScanner.SamePath(d, a));

        release.SetResult();
        var done = await running;
        Assert.Equal(RepoCommandStatus.Succeeded, done.Status);
        Assert.False(svc.IsBusy(a));
        Assert.Equal(RepoCommandStatus.Succeeded, svc.LastCommandResult(a)!.Status);
    }

    // ── Real cmd.exe (Windows CI) ───────────────────────────────────────

    [Fact]
    public async Task Real_cmd_hidden_captures_output_and_exit_code()
    {
        var runner = new ShellCommandRunner();
        var ok = await runner.RunAsync(Cmd("echo", "echo hello && echo second"), _root, TimeSpan.FromSeconds(30));
        Assert.Equal(0, ok.ExitCode);
        Assert.Contains("hello", ok.Output);
        Assert.Contains("second", ok.Output);

        var bad = await runner.RunAsync(Cmd("fail", "echo oops 1>&2 & exit /b 3"), _root, TimeSpan.FromSeconds(30));
        Assert.Equal(3, bad.ExitCode);
        Assert.Contains("oops", bad.Output);

        var cwd = await runner.RunAsync(Cmd("cwd", "cd"), _root, TimeSpan.FromSeconds(30));
        Assert.Contains(Path.GetFileName(_root), cwd.Output);
    }

    [Fact]
    public async Task Real_cmd_does_not_wait_for_input_and_stops_at_the_timeout()
    {
        var runner = new ShellCommandRunner();
        var input = await runner.RunAsync(Cmd("pause", "pause"), _root, TimeSpan.FromSeconds(30));
        Assert.False(input.TimedOut);

        var slow = await runner.RunAsync(Cmd("slow", "ping -n 30 127.0.0.1 > nul"), _root, TimeSpan.FromSeconds(2));
        Assert.True(slow.TimedOut);
    }

    [Fact]
    public async Task Missing_folder_is_a_start_error()
    {
        var r = await new ShellCommandRunner().RunAsync(Cmd("x", "echo hi"), Path.Combine(_root, "nope"), TimeSpan.FromSeconds(5));
        Assert.NotNull(r.StartError);
    }
}
