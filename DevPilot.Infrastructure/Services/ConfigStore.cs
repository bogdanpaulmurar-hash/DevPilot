using DevPilot.Core.Config;
using DevPilot.Core.Interfaces;
using DevPilot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// %APPDATA%\DevPilot\config.json — atomic writes, rolling backup (config.json.bak),
/// automatic v1 → v2 migration (original kept as config.v1.bak.json) and no silent data loss.
/// </summary>
public sealed class ConfigStore : IConfigStore, IDisposable
{
    private readonly ILogger<ConfigStore> _logger;
    private readonly object _gate = new();
    private readonly SynchronizationContext? _sync;
    private readonly Timer _debounce;
    private bool _dirty;

    public AppSettings Current { get; private set; } = Defaults.Create();
    public string ConfigPath { get; }
    public string DataFolder { get; }
    public string? LoadWarning { get; private set; }

    public event EventHandler<ConfigArea>? Changed;

    public ConfigStore(ILogger<ConfigStore> logger)
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevPilot"), logger) { }

    public ConfigStore(string dataFolder, ILogger<ConfigStore>? logger = null)
    {
        _logger = logger ?? NullLogger<ConfigStore>.Instance;
        DataFolder = dataFolder;
        Directory.CreateDirectory(DataFolder);
        ConfigPath = Path.Combine(DataFolder, "config.json");
        _sync = SynchronizationContext.Current;
        _debounce = new Timer(_ => FlushFromTimer(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Load()
    {
        LoadWarning = null;
        if (!File.Exists(ConfigPath))
        {
            Current = Defaults.Create();
            Save();
            return;
        }

        if (TryLoadFile(ConfigPath, out var settings, out var migrated, out var error))
        {
            if (migrated)
            {
                var backup = Path.Combine(DataFolder, "config.v1.bak.json");
                if (!File.Exists(backup)) File.Copy(ConfigPath, backup);
                _logger.LogInformation("Configuration migrated to schema v2 (backup: {Backup})", backup);
            }
            Current = settings!;
            if (migrated) Save();
            return;
        }

        // Keep the broken file for inspection, try the last good copy.
        var bad = Path.Combine(DataFolder, $"config.bad-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        try
        {
            File.Copy(ConfigPath, bad, overwrite: true);
            File.Delete(ConfigPath); // so the next save does not rotate the broken file into .bak
        }
        catch (IOException) { }
        var lastGood = ConfigPath + ".bak";
        if (File.Exists(lastGood) && TryLoadFile(lastGood, out settings, out _, out _))
        {
            Current = settings!;
            LoadWarning = $"config.json could not be read ({error}). The last good copy was loaded; the broken file was kept as {Path.GetFileName(bad)}.";
        }
        else
        {
            Current = Defaults.Create();
            LoadWarning = $"config.json could not be read ({error}). Defaults were loaded; the broken file was kept as {Path.GetFileName(bad)}.";
        }
        _logger.LogWarning("{Warning}", LoadWarning);
        Save();
    }

    private bool TryLoadFile(string path, out AppSettings? settings, out bool migrated, out string? error)
    {
        settings = null; migrated = false; error = null;
        try
        {
            var json = File.ReadAllText(path);
            settings = ConfigSerializer.Load(json, out migrated);
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            _dirty = false;
            var json = ConfigSerializer.Serialize(Current);
            var tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(ConfigPath))
                File.Replace(tmp, ConfigPath, ConfigPath + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(tmp, ConfigPath);
        }
    }

    public void Touch(ConfigArea area)
    {
        _dirty = true;
        _debounce.Change(600, Timeout.Infinite);
        Changed?.Invoke(this, area);
    }

    private void FlushFromTimer()
    {
        if (!_dirty) return;
        void DoSave()
        {
            try { if (_dirty) Save(); }
            catch (Exception ex) { _logger.LogError(ex, "Saving the configuration failed"); }
        }
        if (_sync is not null) _sync.Post(_ => DoSave(), null);
        else DoSave();
    }

    /// <summary>Writes pending changes immediately (call on exit).</summary>
    public void Flush()
    {
        _debounce.Change(Timeout.Infinite, Timeout.Infinite);
        if (_dirty) Save();
    }

    public void Export(string path)
    {
        File.WriteAllText(path, ConfigSerializer.Serialize(Current));
    }

    public void Import(string path)
    {
        var json = File.ReadAllText(path);
        var imported = ConfigSerializer.Load(json, out _);
        var backup = Path.Combine(DataFolder, $"config.before-import-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        if (File.Exists(ConfigPath)) File.Copy(ConfigPath, backup, overwrite: true);
        Current = imported;
        Save();
        Changed?.Invoke(this, ConfigArea.All);
    }

    public void Reset()
    {
        var backup = Path.Combine(DataFolder, $"config.before-reset-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        if (File.Exists(ConfigPath)) File.Copy(ConfigPath, backup, overwrite: true);
        Current = Defaults.Create();
        Save();
        Changed?.Invoke(this, ConfigArea.All);
    }

    public void Dispose()
    {
        try { Flush(); } catch (Exception ex) { _logger.LogError(ex, "Final save failed"); }
        _debounce.Dispose();
    }
}
