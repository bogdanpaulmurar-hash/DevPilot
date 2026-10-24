using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

public enum ConfigArea { All, Tools, Groups, Hub, Theme, KeepAwake, Startup, Monitor, Hotkeys, Window, Backup }

/// <summary>Single source of truth for the configuration. Every editor writes here; the hub listens to <see cref="Changed"/>.</summary>
public interface IConfigStore
{
    AppSettings Current { get; }
    string ConfigPath { get; }
    string DataFolder { get; }
    /// <summary>Set when the last load had to fall back (e.g. corrupt JSON); the original file is kept as a .bad copy.</summary>
    string? LoadWarning { get; }

    event EventHandler<ConfigArea>? Changed;

    void Load();
    void Save();
    /// <summary>Marks the area as changed, notifies listeners and saves after a short debounce.</summary>
    void Touch(ConfigArea area);
    void Export(string path);
    void Import(string path);
    void Reset();
    /// <summary>Replaces the whole configuration (restore), saves it and notifies with <see cref="ConfigArea.All"/>.</summary>
    void Replace(AppSettings settings);
}
