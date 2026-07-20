// ============================================================
// DevPilot — IT Automation Hub
// SettingsManagerTests.cs — Unit tests for SettingsManager
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System;
using System.IO;
using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using Xunit;

namespace DevPilot.Tests.Services;

public class SettingsManagerTests : IDisposable
{
    private readonly string _tempPath;
    private readonly SettingsManager _sut;

    public SettingsManagerTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        _sut = new SettingsManager(_tempPath);
    }

    [Fact]
    public void Load_WhenFileNotExists_ReturnsDefaults()
    {
        _sut.Load();

        var settings = _sut.Current;

        Assert.NotNull(settings);
        Assert.Equal(60, settings.JigglerIntervalSec);
        Assert.Equal("dark", settings.Theme);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesJigglerIntervalSec()
    {
        _sut.Load();
        _sut.Current.JigglerIntervalSec = 120;
        _sut.Save();

        var sut2 = new SettingsManager(_tempPath);
        sut2.Load();

        Assert.Equal(120, sut2.Current.JigglerIntervalSec);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesTheme()
    {
        _sut.Load();
        _sut.Current.Theme = "light";
        _sut.Save();

        var sut2 = new SettingsManager(_tempPath);
        sut2.Load();

        Assert.Equal("light", sut2.Current.Theme);
    }

    [Fact]
    public void Reset_RestoresDefaults()
    {
        _sut.Load();
        _sut.Current.JigglerIntervalSec = 999;
        _sut.Current.Theme = "light";
        _sut.Reset();

        Assert.Equal(60,     _sut.Current.JigglerIntervalSec);
        Assert.Equal("dark", _sut.Current.Theme);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesScreenshotFolder()
    {
        _sut.Load();
        _sut.Current.ScreenshotSaveFolder = @"C:\Temp\Screenshots";
        _sut.Save();

        var sut2 = new SettingsManager(_tempPath);
        sut2.Load();

        Assert.Equal(@"C:\Temp\Screenshots", sut2.Current.ScreenshotSaveFolder);
    }

    public void Dispose()
    {
        try { if (File.Exists(_tempPath)) File.Delete(_tempPath); }
        catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }
}
