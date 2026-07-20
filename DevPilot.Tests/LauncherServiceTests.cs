// ============================================================
// DevPilot — IT Automation Hub
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using DevPilot.Infrastructure.Services;
using DevPilot.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevPilot.Tests;

/// <summary>
/// Unit tests for <see cref="LauncherService"/>.
/// Tests that trigger <c>Process.Start</c> are avoided; only label, type
/// enumeration, and cancellation behaviour are verified here.
/// </summary>
public sealed class LauncherServiceTests
{
    private static LauncherService CreateSut()
        => new(NullLogger<LauncherService>.Instance);

    // ── GetTypeLabel ──────────────────────────────────────────────────────

    [Fact]
    public void GetTypeLabel_Executable_ContainsExecutable()
    {
        var label = CreateSut().GetTypeLabel(AppEntryType.Executable);
        Assert.Contains("Executable", label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetTypeLabel_Folder_ContainsFolder()
    {
        var label = CreateSut().GetTypeLabel(AppEntryType.Folder);
        Assert.Contains("Folder", label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetTypeLabel_Url_ContainsUrl()
    {
        var label = CreateSut().GetTypeLabel(AppEntryType.Url);
        Assert.Contains("URL", label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetTypeLabel_PowerShell_ContainsPowerShell()
    {
        var label = CreateSut().GetTypeLabel(AppEntryType.PowerShell);
        Assert.Contains("PowerShell", label, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AppEntryType.Executable)]
    [InlineData(AppEntryType.Folder)]
    [InlineData(AppEntryType.Url)]
    [InlineData(AppEntryType.PowerShell)]
    [InlineData(AppEntryType.Batch)]
    [InlineData(AppEntryType.Python)]
    [InlineData(AppEntryType.VBScript)]
    [InlineData(AppEntryType.ShellCommand)]
    [InlineData(AppEntryType.Document)]
    [InlineData(AppEntryType.NetworkShare)]
    [InlineData(AppEntryType.Ssh)]
    [InlineData(AppEntryType.Wsl)]
    public void GetTypeLabel_AllSupportedTypes_ReturnNonEmptyString(AppEntryType type)
    {
        var label = CreateSut().GetTypeLabel(type);
        Assert.False(string.IsNullOrWhiteSpace(label));
    }

    // ── GetSupportedTypes ─────────────────────────────────────────────────

    [Fact]
    public void GetSupportedTypes_ContainsAllEnumValues()
    {
        var supported = CreateSut().GetSupportedTypes();
        foreach (var type in Enum.GetValues<AppEntryType>())
            Assert.Contains(type, supported);
    }

    [Fact]
    public void GetSupportedTypes_ReturnsReadOnlyList()
    {
        var supported = CreateSut().GetSupportedTypes();
        Assert.NotNull(supported);
        Assert.True(supported.Count > 0);
    }

    // ── LaunchAsync — cancellation ────────────────────────────────────────

    [Fact]
    public async Task LaunchAsync_WhenTokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        var sut   = CreateSut();
        var entry = new AppEntry
        {
            Id   = "test-1",
            Name = "TestApp",
            Type = AppEntryType.Executable,
            Path = @"C:\DoesNotExist\fake.exe"
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel before calling LaunchAsync

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.LaunchAsync(entry, cts.Token));
    }
}