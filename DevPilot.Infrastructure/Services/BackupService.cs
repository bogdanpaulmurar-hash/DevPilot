using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DevPilot.Core.Backup;
using DevPilot.Core.Config;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Launch;
using DevPilot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevPilot.Infrastructure.Services;

public sealed record BackupResult(bool Success, string? Path, string? Error)
{
    public static BackupResult Fail(string error) => new(false, null, error);
}

public sealed record RestoreResult(bool Success, string? Error, string? SafetyBackup, RestorePreview? Preview);

/// <summary>
/// Writes DevPilot backups (.zip with config.json, the picked logos and manifest.json) into the chosen folder,
/// keeps the newest N, and restores them (with a safety backup first).
/// </summary>
public sealed class BackupService
{
    private readonly IConfigStore _config;
    private readonly ILogger<BackupService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Overridable for tests; the Windows user folder used to adapt paths.</summary>
    public string UserFolder { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public BackupService(IConfigStore config, ILogger<BackupService>? logger = null)
    {
        _config = config;
        _logger = logger ?? NullLogger<BackupService>.Instance;
    }

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DevPilot Backups");

    /// <summary>The folder backups go to (the setting, or Documents\DevPilot Backups).</summary>
    public string Folder => ResolveFolder(_config.Current.Backup.Folder);

    public static string ResolveFolder(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting)) return DefaultFolder;
        try { return PathTokens.Expand(setting.Trim().Trim('"'), null); }
        catch (ArgumentException) { return DefaultFolder; }
    }

    /// <summary>Backups in the current folder, newest first.</summary>
    public IReadOnlyList<BackupFileInfo> List()
    {
        try
        {
            var folder = Folder;
            if (!Directory.Exists(folder)) return [];
            var list = new List<BackupFileInfo>();
            foreach (var f in Directory.GetFiles(folder, BackupPlanner.SearchPattern))
                if (BackupPlanner.TryParse(f, out var info)) list.Add(info);
            return list.OrderByDescending(i => i.CreatedAt).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>Runs the automatic backup if the setting says it is due (call once after start).</summary>
    public async Task<BackupResult?> RunAutomaticIfDueAsync()
    {
        var b = _config.Current.Backup;
        if (!BackupPlanner.IsDue(b.Mode, b.LastBackup, DateTime.Now)) return null;
        return await BackupNowAsync(automatic: true);
    }

    /// <summary>Makes a backup now. The configuration is captured on the calling thread; the files are written in the background.</summary>
    public async Task<BackupResult> BackupNowAsync(bool automatic = false) => await CreateAsync(automatic, beforeRestore: false);

    private async Task<BackupResult> CreateAsync(bool automatic, bool beforeRestore)
    {
        // Snapshot on the caller's thread: the UI may keep editing while the zip is written.
        var json = ConfigSerializer.Serialize(_config.Current);
        var snapshot = ConfigSerializer.Load(json, out _);
        var folder = ResolveFolder(snapshot.Backup.Folder);
        int keep = snapshot.Backup.Keep;
        var dataFolder = _config.DataFolder;
        var userFolder = UserFolder;

        await _gate.WaitAsync().ConfigureAwait(true);
        BackupResult result;
        try
        {
            result = await Task.Run(() => Write(folder, keep, json, snapshot, dataFolder, userFolder, automatic, beforeRestore)).ConfigureAwait(true);
        }
        finally { _gate.Release(); }

        if (result.Success && !beforeRestore)
        {
            _config.Current.Backup.LastBackup = DateTime.Now;
            _config.Touch(ConfigArea.Backup);
        }
        return result;
    }

    private BackupResult Write(string folder, int keep, string json, AppSettings snapshot, string dataFolder, string userFolder,
        bool automatic, bool beforeRestore)
    {
        string? tmp = null;
        try
        {
            Directory.CreateDirectory(folder);
            var created = DateTime.Now;
            string target;
            for (var t = created; ; t = t.AddSeconds(1))
            {
                target = Path.Combine(folder, BackupPlanner.FileName(t, beforeRestore));
                if (!File.Exists(target)) break;
            }
            tmp = target + ".tmp";

            var manifest = new BackupManifest
            {
                AppVersion = LaunchPlanner.AppVersion,
                CreatedAt = created,
                Machine = Environment.MachineName,
                UserFolder = userFolder,
                Automatic = automatic,
                BeforeRestore = beforeRestore,
                Tools = snapshot.Tools.Count(t => !t.IsSeparator),
                Groups = snapshot.Groups.Count,
            };

            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteText(zip, BackupManifest.ConfigEntry, json);
                foreach (var logo in BackupPlanner.PlanLogos(snapshot, File.Exists))
                {
                    try
                    {
                        zip.CreateEntryFromFile(logo.Source, logo.Entry, CompressionLevel.Optimal);
                        manifest.Logos.Add(new BackupLogo { Entry = logo.Entry, IconPaths = [.. logo.IconPaths], Source = logo.Source });
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // e.g. a logo in a Drive folder that is not synced: the backup still works, the tool keeps its path.
                        _logger.LogWarning("Backup: logo {Logo} skipped ({Error})", logo.Source, ex.Message);
                    }
                }
                var startupBackup = Path.Combine(dataFolder, "startup-backup");
                if (Directory.Exists(startupBackup))
                    foreach (var f in Directory.GetFiles(startupBackup))
                    {
                        try { zip.CreateEntryFromFile(f, BackupManifest.StartupFolder + Path.GetFileName(f), CompressionLevel.Optimal); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    }
                WriteText(zip, BackupManifest.FileName, JsonSerializer.Serialize(manifest, ConfigSerializer.Options));
            }
            File.Move(tmp, target, overwrite: true);
            tmp = null;
            _logger.LogInformation("Backup written: {Path} (automatic={Auto}, logos={Logos})", target, automatic, manifest.Logos.Count);

            foreach (var old in BackupPlanner.ToDelete(Directory.GetFiles(folder, BackupPlanner.SearchPattern), keep))
            {
                try { File.Delete(old); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning("Backup: could not remove old backup {Path} ({Error})", old, ex.Message);
                }
            }
            return new BackupResult(true, target, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger.LogError(ex, "Backup to {Folder} failed", folder);
            return BackupResult.Fail(ex.Message);
        }
        finally
        {
            if (tmp is not null) try { File.Delete(tmp); } catch (Exception) { }
        }
    }

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(text);
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var r = new StreamReader(entry.Open(), Encoding.UTF8);
        return r.ReadToEnd();
    }

    /// <summary>Reads and checks a backup. Throws <see cref="InvalidDataException"/> with a readable message when it is not a usable DevPilot backup.</summary>
    public static (BackupManifest Manifest, AppSettings Settings) Read(ZipArchive zip)
    {
        var mEntry = zip.GetEntry(BackupManifest.FileName)
            ?? throw new InvalidDataException("This file is not a DevPilot backup (manifest.json is missing).");
        BackupManifest manifest;
        try { manifest = JsonSerializer.Deserialize<BackupManifest>(ReadText(mEntry), ConfigSerializer.Options) ?? new BackupManifest(); }
        catch (JsonException) { throw new InvalidDataException("This file is not a DevPilot backup (manifest.json cannot be read)."); }
        if (!string.Equals(manifest.App, BackupManifest.AppName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This file is not a DevPilot backup.");
        if (manifest.Format > BackupManifest.CurrentFormat)
            throw new InvalidDataException("This backup was made by a newer DevPilot. Update DevPilot first.");
        var cEntry = zip.GetEntry(BackupManifest.ConfigEntry)
            ?? throw new InvalidDataException("The backup has no config.json.");
        AppSettings settings;
        try { settings = ConfigSerializer.Load(ReadText(cEntry), out _); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The configuration in the backup cannot be read: " + ex.Message);
        }
        manifest.Logos ??= [];
        return (manifest, settings);
    }

    /// <summary>What restoring this backup would change. Throws <see cref="InvalidDataException"/> or <see cref="IOException"/>.</summary>
    public RestorePreview Preview(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var (manifest, fromBackup) = Read(zip);
        var current = _config.Current;
        var merged = BackupRestore.Merge(current, fromBackup, null, manifest.UserFolder, UserFolder,
            current.Backup.AdaptUserFolder, out int adapted);
        return BackupRestore.Preview(current, merged, manifest, adapted, UserFolder, PathExists);
    }

    /// <summary>Makes a safety backup, restores the logos into the data folder and replaces the configuration.</summary>
    public async Task<RestoreResult> RestoreAsync(string zipPath)
    {
        AppSettings fromBackup;
        BackupManifest manifest;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            (manifest, fromBackup) = Read(zip);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new RestoreResult(false, ex.Message, null, null);
        }

        var safety = await CreateAsync(automatic: false, beforeRestore: true);
        if (!safety.Success)
            return new RestoreResult(false, "The backup of the current configuration could not be made, so nothing was restored: " + safety.Error, null, null);

        Dictionary<string, string> logoMap;
        try
        {
            var logosDir = Path.Combine(_config.DataFolder, "logos");
            logoMap = await Task.Run(() => ExtractLogos(zipPath, manifest, logosDir)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new RestoreResult(false, "The logos could not be restored, so nothing was changed: " + ex.Message, safety.Path, null);
        }

        var current = _config.Current;
        var merged = BackupRestore.Merge(current, fromBackup, logoMap, manifest.UserFolder, UserFolder,
            current.Backup.AdaptUserFolder, out int adapted);
        var preview = BackupRestore.Preview(current, merged, manifest, adapted, UserFolder, PathExists);
        _config.Replace(merged);
        _logger.LogInformation("Restored {Backup} (safety copy {Safety})", zipPath, safety.Path);
        return new RestoreResult(true, null, safety.Path, preview);
    }

    /// <summary>Copies the backup's logos into <paramref name="logosDir"/>; returns old IconPath → new file.
    /// An identical file already there is reused, a different one with the same name gets "-2".</summary>
    private static Dictionary<string, string> ExtractLogos(string zipPath, BackupManifest manifest, string logosDir)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (manifest.Logos.Count == 0) return map;
        using var zip = ZipFile.OpenRead(zipPath);
        Directory.CreateDirectory(logosDir);
        foreach (var logo in manifest.Logos)
        {
            var entry = zip.GetEntry(logo.Entry);
            if (entry is null || logo.IconPaths is null || logo.IconPaths.Count == 0) continue;
            byte[] bytes;
            using (var s = entry.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                bytes = ms.ToArray();
            }
            var name = Path.GetFileName(entry.FullName);
            if (string.IsNullOrWhiteSpace(name)) continue;
            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            string target = Path.Combine(logosDir, name);
            for (int i = 2; File.Exists(target) && !File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes); i++)
                target = Path.Combine(logosDir, $"{stem}-{i}{ext}");
            if (!File.Exists(target)) File.WriteAllBytes(target, bytes);
            foreach (var raw in logo.IconPaths)
                if (!string.IsNullOrWhiteSpace(raw)) map[raw.Trim()] = target;
        }
        return map;
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);
}
