using System.IO.Compression;
using DevPilot.Core.Backup;
using DevPilot.Core.Config;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

public class BackupTests
{
    [Fact]
    public void File_names_round_trip()
    {
        var t = new DateTime(2026, 10, 24, 19, 5, 7);
        var name = BackupPlanner.FileName(t);
        Assert.Equal("DevPilot-backup-20261024-190507.zip", name);
        Assert.True(BackupPlanner.TryParse(@"C:\x\" + name, out var info));
        Assert.Equal(t, info.CreatedAt);
        Assert.False(info.BeforeRestore);

        Assert.True(BackupPlanner.TryParse(BackupPlanner.FileName(t, beforeRestore: true), out var safety));
        Assert.True(safety.BeforeRestore);
        Assert.Equal(t, safety.CreatedAt);

        Assert.False(BackupPlanner.TryParse("DevPilot-backup-latest.zip", out _));
        Assert.False(BackupPlanner.TryParse("other.zip", out _));
    }

    [Fact]
    public void Rotation_keeps_newest_and_ignores_other_files()
    {
        var start = new DateTime(2026, 10, 1, 8, 0, 0);
        var files = Enumerable.Range(0, 6).Select(i => BackupPlanner.FileName(start.AddDays(i))).ToList();
        files.AddRange(Enumerable.Range(0, 5).Select(i => BackupPlanner.FileName(start.AddHours(i), beforeRestore: true)));
        files.Add("my-notes.zip");
        files.Add("DevPilot-backup-manual-copy.zip");

        var delete = BackupPlanner.ToDelete(files, keep: 3);

        Assert.Equal(3 + 2, delete.Count);
        Assert.Contains(BackupPlanner.FileName(start), delete);
        Assert.DoesNotContain(BackupPlanner.FileName(start.AddDays(5)), delete);
        Assert.DoesNotContain("my-notes.zip", delete);
        Assert.DoesNotContain("DevPilot-backup-manual-copy.zip", delete);
        // keep is clamped to 1..10
        Assert.Equal(6 - 1 + 2, BackupPlanner.ToDelete(files, keep: 0).Count);
        Assert.Equal(2, BackupPlanner.ToDelete(files, keep: 50).Count);
    }

    [Fact]
    public void Automatic_backup_schedule()
    {
        var now = new DateTime(2026, 10, 24, 9, 0, 0);
        Assert.False(BackupPlanner.IsDue(BackupMode.Manual, null, now));
        Assert.True(BackupPlanner.IsDue(BackupMode.Daily, null, now));
        Assert.True(BackupPlanner.IsDue(BackupMode.Daily, now.AddDays(-1), now));
        Assert.False(BackupPlanner.IsDue(BackupMode.Daily, now.AddHours(-2), now));
        Assert.True(BackupPlanner.IsDue(BackupMode.EveryStart, now.AddMinutes(-1), now));
    }

    [Fact]
    public void Only_existing_image_logos_are_planned_once()
    {
        var s = new AppSettings();
        s.Tools.Add(new ToolEntry { Name = "A", IconPath = @"C:\L\one\logo.png" });
        s.Tools.Add(new ToolEntry { Name = "B", IconPath = @"C:\L\two\logo.png" });
        s.Tools.Add(new ToolEntry { Name = "C", IconPath = @"C:\L\one\logo.png" });
        s.Tools.Add(new ToolEntry { Name = "D", IconPath = @"C:\Apps\tool.exe" });
        s.Tools.Add(new ToolEntry { Name = "E", IconPath = @"C:\L\missing.ico" });

        var plan = BackupPlanner.PlanLogos(s, p => !p.Contains("missing"));

        Assert.Equal(2, plan.Count);
        Assert.Equal("logos/logo.png", plan[0].Entry);
        Assert.Equal("logos/logo-2.png", plan[1].Entry);
        Assert.Single(plan[0].IconPaths);
    }

    [Fact]
    public void User_folder_is_replaced_only_as_a_whole_path_prefix()
    {
        const string old = @"C:\Users\bob";
        const string now = @"C:\Users\alice";
        Assert.Equal(@"C:\Users\alice\Dev\x.py", UserFolderRemap.Replace(@"C:\Users\bob\Dev\x.py", old, now));
        Assert.Equal(@"c:\users\alice", UserFolderRemap.Replace(@"c:\users\BOB", old, now).ToLowerInvariant());
        Assert.Equal(@"--out ""C:\Users\alice\out"" --x", UserFolderRemap.Replace(@"--out ""C:\Users\bob\out"" --x", old, now));
        Assert.Equal(@"C:\Users\bobby\x", UserFolderRemap.Replace(@"C:\Users\bobby\x", old, now));
        Assert.Equal(@"D:\C:\Users\bob\x".Length, UserFolderRemap.Replace(@"D:\C:\Users\bob\x", old, now).Length);
        Assert.Equal(@"https://example.test", UserFolderRemap.Replace(@"https://example.test", old, now));
        Assert.Equal(@"C:\Users\bob\x", UserFolderRemap.Replace(@"C:\Users\bob\x", old, old));
    }

    [Fact]
    public void Merge_keeps_local_settings_maps_logos_and_adapts_paths()
    {
        var current = new AppSettings();
        current.Startup.StartWithWindows = true;
        current.Window.Width = 1500;
        current.Backup.Folder = @"D:\Backups";
        current.Backup.Keep = 4;

        var backup = new AppSettings { Theme = ThemeMode.Light };
        backup.Groups.Add(new ToolGroup { Id = "g1", Title = "Work" });
        backup.Tools.Add(new ToolEntry
        {
            Id = "t1", Name = "Tool", GroupId = "g1", Kind = ToolKind.Python,
            Path = @"C:\Users\bob\Dev\tool\main.py", IconPath = @"C:\Users\bob\Pictures\tool.png",
            Actions = [new ToolAction { Kind = ActionKind.Folder, Value = @"C:\Users\bob\Out" }],
        });
        backup.Startup.StartWithWindows = false;
        backup.Backup.Folder = @"E:\Elsewhere";

        var logos = new Dictionary<string, string> { [@"C:\Users\bob\Pictures\tool.png"] = @"C:\Users\alice\AppData\Roaming\DevPilot\logos\tool.png" };
        var merged = BackupRestore.Merge(current, backup, logos, @"C:\Users\bob", @"C:\Users\alice", adaptUserFolder: true, out int adapted);

        Assert.Equal(ThemeMode.Light, merged.Theme);
        Assert.True(merged.Startup.StartWithWindows);
        Assert.Equal(1500, merged.Window.Width);
        Assert.Equal(@"D:\Backups", merged.Backup.Folder);
        Assert.Equal(4, merged.Backup.Keep);
        var t = Assert.Single(merged.Tools);
        Assert.Equal(@"C:\Users\alice\Dev\tool\main.py", t.Path);
        Assert.Equal(@"C:\Users\alice\AppData\Roaming\DevPilot\logos\tool.png", t.IconPath);
        Assert.Equal(@"C:\Users\alice\Out", t.Actions[0].Value);
        Assert.Equal(2, adapted);

        var kept = BackupRestore.Merge(current, backup, null, @"C:\Users\bob", @"C:\Users\alice", adaptUserFolder: false, out int none);
        Assert.Equal(0, none);
        Assert.Equal(@"C:\Users\bob\Dev\tool\main.py", kept.Tools[0].Path);
    }

    [Fact]
    public void Preview_counts_changes_and_missing_paths()
    {
        var current = new AppSettings();
        current.Groups.Add(new ToolGroup { Id = "g1", Title = "Work" });
        current.Tools.Add(new ToolEntry { Id = "a", Name = "Same", GroupId = "g1", Kind = ToolKind.Url, Path = "https://example.test" });
        current.Tools.Add(new ToolEntry { Id = "gone", Name = "Gone", GroupId = "g1", Kind = ToolKind.Url, Path = "https://example.test/2" });
        Normalizer.Normalize(current);

        var backup = BackupRestore.Clone(current);
        backup.Tools.RemoveAll(t => t.Id == "gone");
        backup.Tools.Add(new ToolEntry { Id = "new", Name = "Missing app", GroupId = "g1", Kind = ToolKind.Executable, Path = @"C:\Nowhere\app.exe" });
        backup.Tools.Add(new ToolEntry { Id = "path", Name = "On PATH", GroupId = "g1", Kind = ToolKind.Executable, Path = "notepad.exe" });
        backup.Accent = "#112233";

        var merged = BackupRestore.Merge(current, backup, null, null, null, false, out _);
        var manifest = new BackupManifest { UserFolder = @"C:\Users\bob", Tools = 3, Groups = 1 };
        var p = BackupRestore.Preview(current, merged, manifest, 0, @"C:\Users\alice", _ => false);

        Assert.Equal(2, p.ToolsAdded);
        Assert.Equal(1, p.ToolsRemoved);
        Assert.Contains("theme", p.OtherChanges);
        Assert.Equal(["Missing app"], p.MissingPaths);
        Assert.True(p.FromOtherUserFolder);
        Assert.True(p.HasChanges);
        Assert.Contains("Not found on this PC", p.Describe());
    }

    [Fact]
    public async Task Backup_and_restore_round_trip()
    {
        using var tmp = new TempDir();
        var data = Path.Combine(tmp.Path, "data");
        var backups = Path.Combine(tmp.Path, "backups");
        var logo = Path.Combine(tmp.Path, "pics", "my logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(logo)!);
        File.WriteAllBytes(logo, new byte[] { 1, 2, 3, 4 });

        var store = new ConfigStore(data);
        store.Load();
        store.Current.Backup.Folder = backups;
        store.Current.Backup.Keep = 2;
        store.Current.Groups.Add(new ToolGroup { Id = "g1", Title = "Work" });
        store.Current.Tools.Add(new ToolEntry { Id = "t1", Name = "Tool", GroupId = "g1", Kind = ToolKind.Url, Path = "https://example.test", IconPath = logo });
        store.Save();

        var service = new BackupService(store) { UserFolder = @"C:\Users\test" };
        var first = await service.BackupNowAsync();
        Assert.True(first.Success, first.Error);
        Assert.NotNull(store.Current.Backup.LastBackup);
        using (var zip = ZipFile.OpenRead(first.Path!))
        {
            Assert.NotNull(zip.GetEntry("config.json"));
            Assert.NotNull(zip.GetEntry("manifest.json"));
            Assert.NotNull(zip.GetEntry("logos/my logo.png"));
            var (manifest, settings) = BackupService.Read(zip);
            Assert.Equal(1, manifest.Tools);
            Assert.Single(manifest.Logos);
            Assert.Single(settings.Tools);
        }

        // Change things, then restore.
        store.Current.Tools.Clear();
        store.Current.Theme = ThemeMode.Light;
        store.Save();
        var preview = service.Preview(first.Path!);
        Assert.Equal(1, preview.ToolsAdded);

        var restored = await service.RestoreAsync(first.Path!);
        Assert.True(restored.Success, restored.Error);
        Assert.True(File.Exists(restored.SafetyBackup));
        var tool = Assert.Single(store.Current.Tools);
        Assert.Equal(ThemeMode.Dark, store.Current.Theme);
        Assert.Equal(Path.Combine(data, "logos", "my logo.png"), tool.IconPath);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(tool.IconPath));
        Assert.Equal(backups, store.Current.Backup.Folder);

        // Rotation: only 2 regular backups stay, the safety copy is separate, foreign files untouched.
        File.WriteAllText(Path.Combine(backups, "notes.txt"), "keep me");
        for (int i = 0; i < 3; i++) Assert.True((await service.BackupNowAsync()).Success);
        var regular = service.List().Where(b => !b.BeforeRestore).ToList();
        Assert.Equal(2, regular.Count);
        Assert.Single(service.List(), b => b.BeforeRestore);
        Assert.True(File.Exists(Path.Combine(backups, "notes.txt")));
    }

    [Fact]
    public void A_foreign_zip_is_rejected()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "other.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            zip.CreateEntry("readme.txt");
        var store = new ConfigStore(Path.Combine(tmp.Path, "data"));
        store.Load();
        var service = new BackupService(store);
        Assert.Throws<InvalidDataException>(() => service.Preview(path));
    }
}
