using DevPilot.Core.Interfaces;
using DevPilot.Core.Repos;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

/// <summary>The sign-in fix: what the visible window runs, and what DevPilot makes of its outcome.</summary>
public sealed class RepoSignInTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "devpilot-signin-" + Guid.NewGuid().ToString("N"));

    public RepoSignInTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private string Repo(string name, string head = "ref: refs/heads/main\n")
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        File.WriteAllText(Path.Combine(dir, ".git", "HEAD"), head);
        return dir;
    }

    // ── The command line ────────────────────────────────────────────────

    [Fact]
    public void Fetch_and_clone_quote_paths_and_the_git_path_only_when_needed()
    {
        Assert.Equal("git -C \"G:\\My Drive\\Dev\\app\" fetch", RepoSignIn.Fetch("git", @"G:\My Drive\Dev\app"));
        Assert.Equal("\"C:\\Program Files\\Git\\cmd\\git.exe\" -C \"D:\\Dev\\app\" fetch", RepoSignIn.Fetch(@"C:\Program Files\Git\cmd\git.exe", @"D:\Dev\app"));
        Assert.Equal("git clone --branch \"main\" -- \"https://example.invalid/me/app.git\" \"D:\\Dev\\app\"",
            RepoSignIn.Clone("git", "https://example.invalid/me/app.git", "main", @"D:\Dev\app"));
        Assert.Equal("git clone -- \"https://example.invalid/me/app.git\" \"D:\\Dev\\app\"",
            RepoSignIn.Clone("git", "https://example.invalid/me/app.git", "", @"D:\Dev\app"));
    }

    [Fact]
    public void Quote_drops_quotes_and_keeps_a_trailing_backslash_from_eating_the_closing_quote()
    {
        Assert.Equal("\"a b\"", RepoSignIn.Quote("a \"b\""));
        Assert.Equal("\"D:\\\\\"", RepoSignIn.Quote("D:\\"));
    }

    [Fact]
    public void Script_runs_every_step_and_fails_as_a_whole_when_one_fails()
    {
        var line = RepoSignIn.Script(["git -C \"a\" fetch", "git clone -- \"u\" \"b\""]);
        Assert.Contains("(git -C \"a\" fetch || set DEVPILOT_SIGNIN_FAILED=1) & (git clone -- \"u\" \"b\" || set DEVPILOT_SIGNIN_FAILED=1)", line);
        Assert.Contains("if defined DEVPILOT_SIGNIN_FAILED (", line);
        Assert.Contains("exit 1)", line);
        Assert.Contains("exit 0)", line);
        // The window waits for a key only after a failure, so the reason can be read.
        Assert.True(line.IndexOf("pause", StringComparison.Ordinal) < line.IndexOf("exit 1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("fatal: unable to access 'https://me:ghp_secret@example.invalid/a.git/'", "fatal: unable to access 'https://example.invalid/a.git/'")]
    [InlineData("git clone -- \"https://me@example.invalid/a.git\" \"D:\\Dev\\a\"", "git clone -- \"https://example.invalid/a.git\" \"D:\\Dev\\a\"")]
    [InlineData("https://example.invalid/a.git", "https://example.invalid/a.git")]
    [InlineData("", "")]
    public void Redact_removes_the_user_part_of_addresses(string text, string expected) =>
        Assert.Equal(expected, RepoSignIn.Redact(text));

    // ── Messages ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("remote: Repository not found.\nfatal: repository 'https://example.invalid/a.git/' not found", "does not exist for the account")]
    [InlineData("fatal: Cannot prompt because user interactivity has been disabled.", "no saved sign-in")]
    [InlineData("fatal: could not read Username for 'https://example.invalid': terminal prompts disabled", "no saved sign-in")]
    [InlineData("git@example.invalid: Permission denied (publickey).", "SSH key")]
    [InlineData("fatal: Authentication failed for 'https://example.invalid/a.git/'", "Sign-in to the remote failed")]
    public void A_failed_sign_in_says_which_kind_and_points_to_Fix(string stderr, string expected)
    {
        var message = RepoPull.Describe(RepoPullStatus.AuthFailed, "", stderr);
        Assert.Contains(expected, message);
        Assert.Contains("Fix…", message);
    }

    // ── Which account ───────────────────────────────────────────────────

    [Theory]
    [InlineData("https://example.invalid/me/app.git", "https://example.invalid", "me")]
    [InlineData("https://Example.invalid/Me/app", "https://example.invalid", "Me")]
    [InlineData("https://other@example.invalid/me/app.git/", "https://example.invalid", "me")]
    public void The_address_tells_the_site_and_the_account_that_owns_the_repository(string url, string site, string owner)
    {
        var account = RepoRemote.Account(url);
        Assert.NotNull(account);
        Assert.Equal(site, account.Value.Site);
        Assert.Equal(owner, account.Value.Owner);
        Assert.Equal($"credential.{site}.username", RepoRemote.UsernameKey(account.Value.Site));
    }

    [Theory]
    [InlineData("git@example.invalid:me/app.git")]
    [InlineData("https://example.invalid/app.git")]
    [InlineData("https://example.invalid/me/app.git & calc")]
    [InlineData("")]
    public void An_address_without_a_plain_owner_gives_no_account(string url) => Assert.Null(RepoRemote.Account(url));

    [Fact]
    public void The_account_git_tried_is_read_from_its_output()
    {
        const string output = "Cloning into 'D:\\Dev\\app'...\nfatal: Cannot prompt because user interactivity has been disabled.\n" +
                              "fatal: could not read Password for 'https://someone-else@example.invalid': terminal prompts disabled";
        Assert.Equal("someone-else", RepoRemote.AttemptedUser(output));
        Assert.Null(RepoRemote.AttemptedUser("fatal: could not read Username for 'https://example.invalid': terminal prompts disabled"));
        Assert.Null(RepoRemote.AttemptedUser(""));

        var message = RepoPull.Describe(RepoPullStatus.AuthFailed, "", output);
        Assert.Contains("\"someone-else\"", message);
        Assert.Contains("Fix…", message);
    }

    [Fact]
    public void A_clone_can_carry_a_setting_into_the_new_clone() =>
        Assert.Equal("git clone -c \"credential.https://example.invalid.username=me\" --branch \"main\" -- \"https://example.invalid/me/app.git\" \"D:\\Dev\\app\"",
            RepoSignIn.Clone("git", "https://example.invalid/me/app.git", "main", @"D:\Dev\app", "credential.https://example.invalid.username=me"));

    [Fact]
    public void A_detached_cloud_copy_clones_the_default_branch()
    {
        Assert.Equal("main", RepoCopies.CloneBranch(Repo("on-main")));
        Assert.Equal(string.Empty, RepoCopies.CloneBranch(Repo("detached", "0123456789abcdef0123456789abcdef01234567\n")));
    }

    // ── The service ─────────────────────────────────────────────────────

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
        public readonly List<string> Calls = [];
        public Task<GitRun> RunAsync(string workingDirectory, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct = default)
        {
            lock (Calls) Calls.Add(string.Join(' ', args));
            return Task.FromResult(args[0] == "remote" ? new GitRun(0, "https://example.invalid/me/app.git\n", "") : new GitRun(0, "", ""));
        }
    }

    private sealed class FakeCommands : ICommandRunner
    {
        public Task<CommandRun> RunAsync(RepoCommand command, string workingDirectory, TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult(new CommandRun(0, ""));
    }

    private sealed class FakeTools : IToolControl
    {
        public bool IsRunning(ToolEntry tool) => false;
        public Task StopAsync(ToolEntry tool) => Task.CompletedTask;
        public Task<LaunchResult> StartAsync(ToolEntry tool) => Task.FromResult(LaunchResult.Ok());
    }

    private sealed class FakeWindow(WindowRun answer) : ISignInWindow
    {
        public readonly List<string> Lines = [];
        public string? Dir;
        public Task<WindowRun> RunAsync(string workingDirectory, string commandLine, TimeSpan timeout, CancellationToken ct = default)
        {
            Dir = workingDirectory;
            Lines.Add(commandLine);
            return Task.FromResult(answer);
        }
    }

    private static RepoSyncService Service(AppSettings settings, FakeGit git, ISignInWindow? window) =>
        new(new FakeConfig(settings), git, new FakeCommands(), new FakeTools(), signIn: window);

    [Fact]
    public async Task The_window_fetches_the_cloud_copy_and_clones_the_missing_local_copy()
    {
        var cloud = Repo("app");
        var local = Path.Combine(_root, "apps", "app");
        var settings = new AppSettings();
        settings.Repos.Items.Add(new RepoConfig { Path = cloud, LocalPath = local });
        var git = new FakeGit();
        var window = new FakeWindow(new WindowRun(0));

        var r = await Service(settings, git, window).SignInAsync(cloud);

        Assert.Equal(RepoSignInOutcome.SignedIn, r.Outcome);
        var line = Assert.Single(window.Lines);
        Assert.Contains($"-C \"{RepoScanner.Normalize(cloud)}\" fetch", line);
        Assert.Contains($"clone --branch \"main\" -- \"https://example.invalid/me/app.git\" \"{RepoScanner.Normalize(local)}\"", line);
        Assert.True(line.IndexOf(" fetch", StringComparison.Ordinal) < line.IndexOf(" clone ", StringComparison.Ordinal));
        // The folder the clone goes into exists, the clone folder itself is left to git.
        Assert.True(Directory.Exists(Path.Combine(_root, "apps")));
        Assert.False(Directory.Exists(local));
        // Nothing in a working tree is touched by DevPilot itself: it only asked for the address.
        Assert.Equal(new[] { "remote get-url origin" }, git.Calls);
    }

    [Fact]
    public async Task A_chosen_account_is_saved_in_each_copy_and_given_to_the_clone()
    {
        var cloud = Repo("app");
        var local = Path.Combine(_root, "apps", "app");
        var settings = new AppSettings();
        settings.Repos.Items.Add(new RepoConfig { Path = cloud, LocalPath = local });
        var git = new FakeGit();
        var window = new FakeWindow(new WindowRun(0));

        var r = await Service(settings, git, window).SignInAsync(cloud, "me");

        Assert.Equal(RepoSignInOutcome.SignedIn, r.Outcome);
        Assert.Equal(new[] { "remote get-url origin", "config --local credential.https://example.invalid.username me" }, git.Calls);
        Assert.Contains("clone -c \"credential.https://example.invalid.username=me\" --branch \"main\"", Assert.Single(window.Lines));
    }

    [Fact]
    public async Task With_both_copies_cloned_the_window_only_fetches()
    {
        var cloud = Repo("app");
        var local = Repo(Path.Combine("apps", "app"));
        var settings = new AppSettings();
        settings.Repos.Items.Add(new RepoConfig { Path = cloud, LocalPath = local });
        var window = new FakeWindow(new WindowRun(0));

        await Service(settings, new FakeGit(), window).SignInAsync(cloud);

        var line = Assert.Single(window.Lines);
        Assert.Contains($"-C \"{RepoScanner.Normalize(cloud)}\" fetch", line);
        Assert.Contains($"-C \"{RepoScanner.Normalize(local)}\" fetch", line);
        Assert.DoesNotContain(" clone ", line);
    }

    [Fact]
    public async Task The_outcome_of_the_window_is_reported()
    {
        var cloud = Repo("app");
        var settings = new AppSettings();

        Assert.Equal(RepoSignInOutcome.Failed, (await Service(settings, new FakeGit(), new FakeWindow(new WindowRun(1))).SignInAsync(cloud)).Outcome);
        Assert.Equal(RepoSignInOutcome.TimedOut, (await Service(settings, new FakeGit(), new FakeWindow(new WindowRun(-1, TimedOut: true))).SignInAsync(cloud)).Outcome);
        var noStart = await Service(settings, new FakeGit(), new FakeWindow(new WindowRun(-1, Error: "cmd.exe is blocked"))).SignInAsync(cloud);
        Assert.Equal(RepoSignInOutcome.CouldNotStart, noStart.Outcome);
        Assert.Equal("cmd.exe is blocked", noStart.Detail);
        Assert.Equal(RepoSignInOutcome.CouldNotStart, (await Service(settings, new FakeGit(), null).SignInAsync(cloud)).Outcome);
    }

    [Fact]
    public async Task A_folder_that_is_not_a_clone_opens_no_window()
    {
        var plain = Path.Combine(_root, "plain");
        Directory.CreateDirectory(plain);
        var window = new FakeWindow(new WindowRun(0));

        var r = await Service(new AppSettings(), new FakeGit(), window).SignInAsync(plain);

        Assert.Equal(RepoSignInOutcome.CouldNotStart, r.Outcome);
        Assert.Empty(window.Lines);
    }
}
