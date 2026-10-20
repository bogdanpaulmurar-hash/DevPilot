using DevPilot.Core.Config;
using DevPilot.Core.Folders;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "devpilot-tests-" + Guid.NewGuid().ToString("N")[..8]);
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { } }
}

public class FolderAndStoreTests
{
    [Fact]
    public void Folder_listing_respects_options()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(System.IO.Path.Combine(tmp.Path, "zSub"));
        Directory.CreateDirectory(System.IO.Path.Combine(tmp.Path, "aSub"));
        foreach (var f in new[] { "b.txt", "a.xlsx", "c.docx" })
            File.WriteAllText(System.IO.Path.Combine(tmp.Path, f), "x");

        var all = FolderBrowser.List(tmp.Path, new FolderOptions());
        Assert.Null(all.Error);
        Assert.Equal(["aSub", "zSub", "a.xlsx", "b.txt", "c.docx"], all.Items.Select(i => i.Name));

        var filtered = FolderBrowser.List(tmp.Path, new FolderOptions { Filter = "*.xlsx; *.docx", ShowFolders = false });
        Assert.Equal(["a.xlsx", "c.docx"], filtered.Items.Select(i => i.Name));

        var limited = FolderBrowser.List(tmp.Path, new FolderOptions { MaxItems = 2 });
        Assert.Equal(2, limited.Items.Count);
        Assert.Equal(3, limited.Truncated);

        Assert.NotNull(FolderBrowser.List(System.IO.Path.Combine(tmp.Path, "missing"), new FolderOptions()).Error);

        var recent = FolderBrowser.RecentFiles(tmp.Path, 2, ".txt", ".docx");
        Assert.Equal(2, recent.Count);
    }

    [Theory]
    [InlineData("report.xlsx", "*.xlsx", true)]
    [InlineData("REPORT.XLSX", "*.xlsx", true)]
    [InlineData("report.xls", "*.xlsx", false)]
    [InlineData("a1.txt", "a?.txt", true)]
    [InlineData("abc", "*", true)]
    public void Wildcards(string text, string pattern, bool expected) =>
        Assert.Equal(expected, FolderBrowser.WildcardMatch(text, pattern));

    [Fact]
    public void Store_creates_defaults_and_round_trips()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Path);
        store.Load();
        Assert.True(File.Exists(store.ConfigPath));
        Assert.NotEmpty(store.Current.Groups);
        store.Current.Accent = "#123456";
        store.Save();
        store.Save(); // second save creates the .bak
        Assert.True(File.Exists(store.ConfigPath + ".bak"));

        var again = new ConfigStore(tmp.Path);
        again.Load();
        Assert.Equal("#123456", again.Current.Accent);
        Assert.Null(again.LoadWarning);
    }

    [Fact]
    public void Store_keeps_corrupt_file_and_uses_last_good_copy()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Path);
        store.Load();
        store.Current.Accent = "#654321";
        store.Save();
        store.Save();
        File.WriteAllText(store.ConfigPath, "{ this is not json");

        var again = new ConfigStore(tmp.Path);
        again.Load();
        Assert.NotNull(again.LoadWarning);
        Assert.Equal("#654321", again.Current.Accent);
        Assert.NotEmpty(Directory.GetFiles(tmp.Path, "config.bad-*.json"));
    }

    [Fact]
    public void Store_migrates_v1_with_backup()
    {
        using var tmp = new TempDir();
        File.WriteAllText(System.IO.Path.Combine(tmp.Path, "config.json"),
            """{ "theme": "dark", "apps": [ { "name": "Calc", "path": "calc.exe", "type": "Executable" } ] }""");
        var store = new ConfigStore(tmp.Path);
        store.Load();
        Assert.True(File.Exists(System.IO.Path.Combine(tmp.Path, "config.v1.bak.json")));
        Assert.Equal("Calc", Assert.Single(store.Current.Tools).Name);
        Assert.Equal(2, ConfigSerializer.DetectSchema(File.ReadAllText(store.ConfigPath)));
    }

    [Fact]
    public void Store_import_and_reset_keep_backups()
    {
        using var tmp = new TempDir();
        var store = new ConfigStore(tmp.Path);
        store.Load();
        var export = System.IO.Path.Combine(tmp.Path, "export.json");
        store.Current.Accent = "#0000FF";
        store.Export(export);
        store.Reset();
        Assert.Equal("#4F8EF7", store.Current.Accent);
        store.Import(export);
        Assert.Equal("#0000FF", store.Current.Accent);
        Assert.NotEmpty(Directory.GetFiles(tmp.Path, "config.before-*.json"));
    }
}
