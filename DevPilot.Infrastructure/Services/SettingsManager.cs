// ============================================================
// DevPilot — IT Automation Hub v1.0
// SettingsManager.cs — JSON read/write via System.Text.Json
// Config path: %APPDATA%\DevPilot\config.json
// Autor: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System;
using System.IO;
using System.Text.Json;
using DevPilot.Core.Interfaces;
using DevPilot.Models;

namespace DevPilot.Infrastructure.Services;

public sealed class SettingsManager : ISettingsManager
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters           = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly string _configPath;

    public AppSettings Current { get; private set; } = new();

    /// <summary>Constructorul default — salveaza in %APPDATA%\DevPilot\config.json</summary>
    public SettingsManager()
    {
        var folder  = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DevPilot");
        Directory.CreateDirectory(folder);
        _configPath = Path.Combine(folder, "config.json");
    }

    /// <summary>Constructorul cu path explicit — folosit in teste.</summary>
    public SettingsManager(string configPath)
    {
        _configPath = configPath;
        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    // ── ISettingsManager ─────────────────────────────────────────────────

    public void Load()
    {
        if (!File.Exists(_configPath))
        {
            Current = BuildDefaults();
            return;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            Current  = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions)
                       ?? BuildDefaults();
        }
        catch
        {
            Current = BuildDefaults();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Current, _jsonOptions);
        File.WriteAllText(_configPath, json);
    }

    public void Reset()
    {
        Current = BuildDefaults();
        Save();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    public static AppSettings BuildDefaults() => new()
    {
        Theme              = "dark",
        JigglerEnabled     = false,
        JigglerIntervalSec = 60,
        HeartbeatEnabled   = false,
        HeartbeatIntervalSec = 30,
        ScreenshotSaveFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "DevPilot")
    };
}
