// ============================================================
// DevPilot — IT Automation Hub v1.0
// StartupService.cs — HKCU autostart via Windows Registry
// Phase: 9 (fulfils Phase 14 specification)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Diagnostics;
using DevPilot.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// Reads/writes HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// to control DevPilot autostart on Windows login.
/// </summary>
public sealed class StartupService : IStartupService
{
    private const string RegistryRunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupKeyName  = "DevPilot"; // same as AppConstants.AppName

    private readonly ILogger<StartupService> _logger;

    public StartupService(ILogger<StartupService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsStartupEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: false);
            return key?.GetValue(StartupKeyName) is not null;
        }
    }

    /// <inheritdoc/>
    public void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true)
                            ?? throw new InvalidOperationException("Cannot open startup registry key.");

            if (enable)
            {
                var exePath = Environment.ProcessPath
                              ?? Process.GetCurrentProcess().MainModule?.FileName
                              ?? string.Empty;
                key.SetValue(StartupKeyName, $"\"{exePath}\"");
                _logger.LogInformation("Startup enabled → {ExePath}", exePath);
            }
            else
            {
                key.DeleteValue(StartupKeyName, throwOnMissingValue: false);
                _logger.LogInformation("Startup disabled.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set startup registry entry (enable={Enable}).", enable);
            throw;
        }
    }
}