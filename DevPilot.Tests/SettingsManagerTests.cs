// ============================================================
// DevPilot — IT Automation Hub
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.IO;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests;

/// <summary>Unit tests for <see cref="SettingsManager"/>. Uses an isolated temp directory.</summary>
public sealed class SettingsManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public SettingsManagerTests()
    {
        _tempDir    = Path.Combine(Path.GetTempPath(), $"DevPilotTests_{Guid.NewGuid():N}");
        _configPath = Path.Combine(_tempDir, "config.json");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private SettingsManager CreateSut() => new(_configPath);

    // ── Load ──────────────────────────────────────────────────────────────

    [Fact]
    public void Load_WhenNoFileExists_ReturnsDefaultDarkTheme()
    {
        var sut = CreateSut();
        sut.Load();
        Assert.Equal("dark", sut.Current.Theme);
    }

    [Fact]
    public void Load_WhenFileIsCorruptJson_FallsBackToDefaults()
    {
        File.WriteAllText(_configPath, "{ not valid json !!!");
        var sut = CreateSut();
        sut.Load();
        Assert.Equal("dark", sut.Current.Theme);
    }

    [Fact]
    public void Load_WhenFileIsEmpty_FallsBackToDefaults()
    {
        File.WriteAllText(_configPath, string.Empty);
        var sut = CreateSut();
        sut.Load();
        Assert.Equal("dark", sut.Current.Theme);
    }

    // ── Save / Load round-trip ────────────────────────────────────────────

    [Fact]
    public void Save_CreatesConfigFile()
    {
        var sut = CreateSut();
        sut.Load();
        sut.Save();
        Assert.True(File.Exists(_configPath));
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public void SaveAndLoad_RoundTrips_Theme(string theme)
    {
        var sut = CreateSut();
        sut.Load();
        sut.Current.Theme = theme;
        sut.Save();

        var sut2 = CreateSut();
        sut2.Load();
        Assert.Equal(theme, sut2.Current.Theme);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_JigglerIntervalSec()
    {
        var sut = CreateSut();
        sut.Load();
        sut.Current.JigglerIntervalSec = 120;
        sut.Save();

        var sut2 = CreateSut();
        sut2.Load();
        Assert.Equal(120, sut2.Current.JigglerIntervalSec);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_JigglerMethod()
    {
        var sut = CreateSut();
        sut.Load();
        sut.Current.JigglerMethod = "Key Press";
        sut.Save();

        var sut2 = CreateSut();
        sut2.Load();
        Assert.Equal("Key Press", sut2.Current.JigglerMethod);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_MinimizeToTray()
    {
        var sut = CreateSut();
        sut.Load();
        sut.Current.MinimizeToTray = false;
        sut.Save();

        var sut2 = CreateSut();
        sut2.Load();
        Assert.False(sut2.Current.MinimizeToTray);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_DataCleansingExePath()
    {
        const string path = @"D:\custom\DataCleansing.exe";
        var sut = CreateSut();
        sut.Load();
        sut.Current.DataCleansingExePath = path;
        sut.Save();

        var sut2 = CreateSut();
        sut2.Load();
        Assert.Equal(path, sut2.Current.DataCleansingExePath);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_ScreenshotSaveFolder()
    {
        const string folder = @"C:\Screenshots\DevPilot";
        var sut = CreateSut();
        sut.Load();
        sut.Current.ScreenshotSaveFolder = folder;
        sut.Save();

        var sut2 = CreateSut();
        sut2.Load();
        Assert.Equal(folder, sut2.Current.ScreenshotSaveFolder);
    }

    // ── Reset ─────────────────────────────────────────────────────────────

    [Fact]
    public void Reset_RestoresDefaultTheme()
    {
        var sut = CreateSut();
        sut.Load();
        sut.Current.Theme = "light";
        sut.Save();

        sut.Reset();

        Assert.Equal("dark", sut.Current.Theme);
    }

    [Fact]
    public void Reset_SavesFileAfterReset()
    {
        var sut = CreateSut();
        sut.Reset();
        Assert.True(File.Exists(_configPath));
    }

    // ── BuildDefaults ─────────────────────────────────────────────────────

    [Fact]
    public void BuildDefaults_DataCleansingExePath_ContainsExpectedExeName()
    {
        var defaults = SettingsManager.BuildDefaults();
        Assert.Contains("DataCleansing.MasterApp.Starter.exe",
            defaults.DataCleansingExePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDefaults_TestClientExePath_ContainsExpectedExeName()
    {
        var defaults = SettingsManager.BuildDefaults();
        Assert.Contains("TestClientUtilityApp.exe",
            defaults.TestClientExePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDefaults_TcMgmtStarterExePath_ContainsExpectedExeName()
    {
        var defaults = SettingsManager.BuildDefaults();
        Assert.Contains("TCMgmtLocalAppStarter.exe",
            defaults.TcMgmtStarterExePath, StringComparison.OrdinalIgnoreCase);
    }
}