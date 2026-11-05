using DevPilot.Core.Interfaces;
using DevPilot.Core.Repos;
using DevPilot.Infrastructure.Services;
using Xunit;

namespace DevPilot.Tests;

public sealed class RepoVersionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "devpilot-version-" + Guid.NewGuid().ToString("N"));

    public RepoVersionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_root, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private string Repo(string name)
    {
        var d = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(d, ".git"));
        File.WriteAllText(Path.Combine(d, ".git", "HEAD"), "ref: refs/heads/main\n");
        return d;
    }

    private static void Write(string repo, string relative, string text)
    {
        var file = Path.Combine(repo, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    // ── Where the version comes from ─────────────────────────────────────

    [Fact]
    public void Csproj_version_of_the_app_project_wins_over_a_library()
    {
        var r = Repo("cs");
        Write(r, "App.Core/App.Core.csproj", "<Project><PropertyGroup><Version>9.9.9</Version></PropertyGroup></Project>");
        Write(r, "App.UI/App.UI.csproj", "<Project><PropertyGroup><OutputType>WinExe</OutputType><Version>2.15.0</Version></PropertyGroup></Project>");
        var v = RepoVersion.Read(r);
        Assert.Equal("2.15.0", v.Version);
        Assert.Equal("App.UI/App.UI.csproj", v.Source);
    }

    [Fact]
    public void Directory_build_props_comes_first_for_dotnet()
    {
        var r = Repo("props");
        Write(r, "Directory.Build.props", "<Project><PropertyGroup><VersionPrefix>1.4.0</VersionPrefix></PropertyGroup></Project>");
        Write(r, "App/App.csproj", "<Project><PropertyGroup><OutputType>Exe</OutputType><Version>0.0.1</Version></PropertyGroup></Project>");
        Assert.Equal("1.4.0", RepoVersion.Read(r).Version);
    }

    [Fact]
    public void Msbuild_property_reference_is_not_a_version()
    {
        var r = Repo("msbuild");
        Write(r, "App.csproj", "<Project><PropertyGroup><Version>$(AppVersion)</Version></PropertyGroup></Project>");
        Assert.Equal(string.Empty, RepoVersion.Read(r).Version);
    }

    [Fact]
    public void Python_package_init_under_src()
    {
        var r = Repo("py-src");
        Write(r, "src/screenpilot/__init__.py", "\"\"\"Doc.\"\"\"\n\n__all__ = [\"__version__\"]\n\n__version__ = \"0.9.9\"\nAPP_NAME = \"X\"\n");
        Write(r, "tests/__init__.py", "__version__ = \"0.0.0\"\n");
        var v = RepoVersion.Read(r);
        Assert.Equal("0.9.9", v.Version);
        Assert.Equal("src/screenpilot/__init__.py", v.Source);
    }

    [Fact]
    public void Python_version_py_one_folder_down()
    {
        var r = Repo("py-version");
        Write(r, "AppPy/version.py", "# -*- coding: utf-8 -*-\n# Schema: MAJOR.MINOR.PATCH\n__version__ = '3.2.1'\n");
        Write(r, "AppPy/rpr/__init__.py", "from .engine import Engine\n");
        Assert.Equal("3.2.1", RepoVersion.Read(r).Version);
    }

    [Fact]
    public void Python_package_one_folder_down_and_typed_assignment()
    {
        var r = Repo("py-pkg");
        Write(r, "amc/__init__.py", "__version__: str = \"1.2.0\"\n");
        Assert.Equal("1.2.0", RepoVersion.Read(r).Version);
    }

    [Fact]
    public void Pyproject_version_in_project_table()
    {
        var r = Repo("toml");
        Write(r, "pyproject.toml", "[build-system]\nrequires = [\"setuptools\"]\n\n[project]\nname = \"x\"\nversion = \"0.5.0\"\n");
        Write(r, "x/__init__.py", "__version__ = \"0.0.1\"\n");
        Assert.Equal("0.5.0", RepoVersion.Read(r).Version);
    }

    [Fact]
    public void Tool_json_version_comes_first()
    {
        var r = Repo("tool");
        Write(r, "devpilot.tool.json", "{ \"name\": \"DocAudit\", \"version\": \"0.1.1\" }");
        Write(r, "docaudit/__init__.py", "__version__ = \"0.0.9\"\n");
        var v = RepoVersion.Read(r);
        Assert.Equal("0.1.1", v.Version);
        Assert.Equal("devpilot.tool.json", v.Source);
    }

    [Fact]
    public void Package_json_and_version_file()
    {
        var js = Repo("js");
        Write(js, "package.json", "{ \"name\": \"x\", \"version\": \"4.0.0\" }");
        Assert.Equal("4.0.0", RepoVersion.Read(js).Version);

        var plain = Repo("plain");
        Write(plain, "VERSION", "v1.0.3\n");
        Assert.Equal("v1.0.3", RepoVersion.Read(plain).Version);
    }

    [Fact]
    public void No_version_falls_back_to_the_commit()
    {
        var r = Repo("commit");
        Write(r, ".git/refs/heads/main", "0123456789abcdef0123456789abcdef01234567\n");
        var v = RepoVersion.Read(r);
        Assert.Equal(string.Empty, v.Version);
        Assert.Equal("0123456", v.Commit);
        Assert.Equal("0123456", v.Display);
    }

    [Fact]
    public void Commit_from_packed_refs_and_detached_head()
    {
        var packed = Repo("packed");
        Write(packed, ".git/packed-refs", "# pack-refs with: peeled fully-peeled sorted\nabcdef0123456789abcdef0123456789abcdef01 refs/heads/main\n");
        Assert.Equal("abcdef0", RepoVersion.ReadCommit(packed));

        var detached = Repo("detached");
        File.WriteAllText(Path.Combine(detached, ".git", "HEAD"), "FEDCBA9876543210FEDCBA9876543210FEDCBA98\n");
        Assert.Equal("fedcba9", RepoVersion.ReadCommit(detached));
    }

    [Fact]
    public void Unreadable_or_missing_folder_gives_nothing()
    {
        Assert.True(RepoVersion.Read(Path.Combine(_root, "missing")).IsEmpty);
        Assert.Equal(string.Empty, RepoVersion.ReadCommit(Repo("empty")));
    }

    // ── What the user sees ───────────────────────────────────────────────

    [Fact]
    public void Change_text()
    {
        var v214 = new RepoVersionInfo("2.14.0", "aaaaaaa");
        var v215 = new RepoVersionInfo("2.15.0", "bbbbbbb");
        Assert.Equal("2.14.0 → 2.15.0", RepoVersion.Change(v214, v215));
        Assert.Equal("2.15.0 (unchanged)", RepoVersion.Change(v215, v215 with { Commit = "ccccccc" }));
        Assert.Equal("aaaaaaa → bbbbbbb", RepoVersion.Change(new("", "aaaaaaa"), new("", "bbbbbbb")));
        Assert.Equal("2.15.0", RepoVersion.Change(RepoVersionInfo.None, v215));
        Assert.Equal(string.Empty, RepoVersion.Change(v214, RepoVersionInfo.None));
    }

    [Fact]
    public void Tip_names_the_source()
    {
        Assert.Equal("Version 2.15.0 from pyproject.toml · commit abc1234", new RepoVersionInfo("2.15.0", "abc1234", "pyproject.toml").Tip);
        Assert.Equal("No version found in the files · commit abc1234", new RepoVersionInfo("", "abc1234").Tip);
    }

    // ── Pull reports old and new version ─────────────────────────────────

    private sealed class BumpingGit(string repo, string newVersion) : IGitRunner
    {
        public Task<GitRun> RunAsync(string workingDirectory, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct = default)
        {
            if (args[0] == "status") return Task.FromResult(new GitRun(0, "", ""));
            Write(repo, "pyproject.toml", $"[project]\nversion = \"{newVersion}\"\n");
            return Task.FromResult(new GitRun(0, "Updating 1a2b..3c4d\nFast-forward\n pyproject.toml | 2 +-\n 1 file changed, 1 insertion(+), 1 deletion(-)\n", ""));
        }
    }

    [Fact]
    public async Task Pull_result_has_old_and_new_version()
    {
        var r = Repo("bump");
        Write(r, "pyproject.toml", "[project]\nversion = \"1.0.0\"\n");
        var result = await new RepoPull(new BumpingGit(r, "1.1.0")).PullAsync(r);
        Assert.Equal(RepoPullStatus.Updated, result.Status);
        Assert.Equal("1.0.0", result.Before.Version);
        Assert.Equal("1.1.0", result.After.Version);
        Assert.Equal("1.0.0 → 1.1.0", result.VersionChange);
        Assert.StartsWith("1.0.0 → 1.1.0 · 1 file changed", result.Message);
    }

    [Fact]
    public async Task Up_to_date_pull_keeps_the_version()
    {
        var r = Repo("same");
        Write(r, "pyproject.toml", "[project]\nversion = \"1.0.0\"\n");
        var git = new FakeGit();
        var result = await new RepoPull(git).PullAsync(r);
        Assert.Equal(RepoPullStatus.UpToDate, result.Status);
        Assert.Equal("1.0.0 (unchanged)", result.VersionChange);
        Assert.Equal("Already up to date.", result.Message);
    }

    private sealed class FakeGit : IGitRunner
    {
        public Task<GitRun> RunAsync(string workingDirectory, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult(new GitRun(0, args[0] == "status" ? "" : "Already up to date.", ""));
    }

    // ── Real git: the commit changes with the pull ───────────────────────

    private static async Task Git(string dir, params string[] args)
    {
        var all = new List<string> { "-c", "user.name=DevPilot Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false", "-c", "init.defaultBranch=main" };
        all.AddRange(args);
        var r = await new GitRunner().RunAsync(dir, all, TimeSpan.FromSeconds(60));
        Assert.False(r.GitMissing, "git is not installed");
        Assert.True(r.ExitCode == 0, $"git {string.Join(' ', args)} failed: {r.StdErr}");
    }

    [Fact]
    public async Task Real_git_pull_shows_version_bump_and_commit()
    {
        var seed = Path.Combine(_root, "seed");
        Directory.CreateDirectory(seed);
        await Git(seed, "init");
        Write(seed, "VERSION", "1.0.0\n");
        await Git(seed, "add", "VERSION");
        await Git(seed, "commit", "-m", "first");
        await Git(_root, "clone", "--bare", seed, "origin.git");
        await Git(_root, "clone", Path.Combine(_root, "origin.git"), "a");
        await Git(_root, "clone", Path.Combine(_root, "origin.git"), "b");
        var a = Path.Combine(_root, "a");
        var b = Path.Combine(_root, "b");

        var before = RepoVersion.Read(b);
        Assert.Equal("1.0.0", before.Version);
        Assert.Equal(7, before.Commit.Length);

        Write(a, "VERSION", "1.1.0\n");
        await Git(a, "commit", "-am", "bump");
        await Git(a, "push");

        var result = await new RepoPull(new GitRunner()).PullAsync(b);
        Assert.Equal(RepoPullStatus.Updated, result.Status);
        Assert.Equal("1.0.0 → 1.1.0", result.VersionChange);
        Assert.NotEqual(before.Commit, result.After.Commit);
        Assert.Equal(RepoVersion.ReadCommit(a), result.After.Commit);
    }
}
