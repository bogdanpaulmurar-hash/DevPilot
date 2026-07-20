// ============================================================
// DevPilot — IT Automation Hub
// LauncherService.cs — Launches AppEntry items
// Phase: 13 (Python: no CMD window, process detached from DevPilot job object)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Diagnostics;
using System.Runtime.InteropServices;
using DevPilot.Core.Interfaces;
using DevPilot.Models;
using Microsoft.Extensions.Logging;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// Launches AppEntry items using the appropriate mechanism
/// based on their AppEntryType.
/// </summary>
public class LauncherService : ILauncherService
{
    private readonly ILogger<LauncherService> _logger;

    // ── Win32 — CREATE_BREAKAWAY_FROM_JOB allows the child to survive DevPilot crash ──
    private const uint CREATE_NO_WINDOW         = 0x08000000;
    private const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
    private const uint DETACHED_PROCESS          = 0x00000008;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int    cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int    dwX, dwY, dwXSize, dwYSize;
        public int    dwXCountChars, dwYCountChars;
        public int    dwFillAttribute;
        public int    dwFlags;
        public short  wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int    dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        string  lpCommandLine,
        IntPtr  lpProcessAttributes,
        IntPtr  lpThreadAttributes,
        bool    bInheritHandles,
        uint    dwCreationFlags,
        IntPtr  lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private static readonly Dictionary<AppEntryType, string> _typeLabels = new()
    {
        { AppEntryType.Executable,   "Executable (.exe)"      },
        { AppEntryType.Folder,       "Folder"                 },
        { AppEntryType.Url,          "URL (browser)"          },
        { AppEntryType.PowerShell,   "PowerShell (.ps1)"      },
        { AppEntryType.Batch,        "Batch (.bat / .cmd)"    },
        { AppEntryType.Python,       "Python (.py)"           },
        { AppEntryType.VBScript,     "VBScript (.vbs)"        },
        { AppEntryType.ShellCommand, "Shell Command"          },
        { AppEntryType.Document,     "Document (default app)" },
        { AppEntryType.NetworkShare, "Network Share"          },
        { AppEntryType.Ssh,          "SSH"                    },
        { AppEntryType.Wsl,          "WSL Command"            },
    };

    public LauncherService(ILogger<LauncherService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> LaunchAsync(AppEntry entry, CancellationToken ct = default)
    {
        _logger.LogInformation("Launching [{Type}] '{Name}' -> {Path}",
            entry.Type, entry.Name, entry.Path);
        try
        {
            await Task.Run(() => Launch(entry), ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch '{Name}'", entry.Name);
            throw;
        }
    }

    private void Launch(AppEntry entry)
    {
        // Python gets a special detached launch to avoid CMD window and survive DevPilot crash
        if (entry.Type == AppEntryType.Python)
        {
            LaunchPythonDetached(entry);
            return;
        }

        ProcessStartInfo psi;

        if (entry.Type == AppEntryType.Executable)
            psi = BuildExe(entry);
        else if (entry.Type == AppEntryType.Folder)
            psi = BuildFolder(entry);
        else if (entry.Type == AppEntryType.Url)
            psi = BuildUrl(entry);
        else if (entry.Type == AppEntryType.PowerShell)
            psi = BuildPowerShell(entry);
        else if (entry.Type == AppEntryType.Batch)
            psi = BuildBatch(entry);
        else if (entry.Type == AppEntryType.VBScript)
            psi = BuildVBScript(entry);
        else if (entry.Type == AppEntryType.ShellCommand)
            psi = BuildShellCommand(entry);
        else if (entry.Type == AppEntryType.Document)
            psi = BuildDocument(entry);
        else if (entry.Type == AppEntryType.NetworkShare)
            psi = BuildNetworkShare(entry);
        else if (entry.Type == AppEntryType.Ssh)
            psi = BuildSsh(entry);
        else if (entry.Type == AppEntryType.Wsl)
            psi = BuildWsl(entry);
        else
            throw new NotSupportedException($"AppEntryType '{entry.Type}' is not supported.");

        if (entry.RunAsAdmin)
            psi.Verb = "runas";

        psi.UseShellExecute = true;
        Process.Start(psi);
    }

    // ── Builders ─────────────────────────────────────────────────────────────

    private static ProcessStartInfo BuildExe(AppEntry e)
        => new ProcessStartInfo { FileName = e.Path, Arguments = e.Arguments };

    private static ProcessStartInfo BuildFolder(AppEntry e)
        => new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + e.Path + "\"" };

    private static ProcessStartInfo BuildUrl(AppEntry e)
        => new ProcessStartInfo { FileName = e.Path };

    private static ProcessStartInfo BuildPowerShell(AppEntry e)
        => new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-ExecutionPolicy Bypass -File \"" + e.Path + "\" " + e.Arguments
        };

    private static ProcessStartInfo BuildBatch(AppEntry e)
        => new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c \"" + e.Path + "\" " + e.Arguments
        };

    private static ProcessStartInfo BuildPython(AppEntry e)
        => new ProcessStartInfo
        {
            FileName = "python.exe",
            Arguments = "\"" + e.Path + "\" " + e.Arguments
        };

    // ── Python detached launcher (no CMD, survives DevPilot exit) ────────────

    /// <summary>
    /// Launches a Python script directly via pythonw.exe (no console window) and
    /// uses Win32 CreateProcess with CREATE_BREAKAWAY_FROM_JOB so the Python
    /// process is not part of DevPilot's Windows Job Object.
    /// This means it will NOT be killed if DevPilot crashes or is closed.
    /// </summary>
    private void LaunchPythonDetached(AppEntry entry)
    {
        // pythonw.exe suppresses the console window entirely.
        // Fall back to python.exe if pythonw is absent on PATH.
        const string pythonw = "pythonw.exe";

        var args = string.IsNullOrWhiteSpace(entry.Arguments)
            ? $"\"{entry.Path}\""
            : $"\"{entry.Path}\" {entry.Arguments}";

        var cmdLine = $"\"{pythonw}\" {args}";

        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };

        // CREATE_BREAKAWAY_FROM_JOB  — leaves DevPilot's job object → survives crash
        // DETACHED_PROCESS           — no inherited console
        // CREATE_NO_WINDOW           — belt-and-suspenders: no console window
        uint flags = CREATE_BREAKAWAY_FROM_JOB | DETACHED_PROCESS | CREATE_NO_WINDOW;

        bool ok = CreateProcess(
            null,
            cmdLine,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            flags,
            IntPtr.Zero,
            null,
            ref si,
            out var pi);

        if (!ok)
        {
            // CreateProcess failed (e.g. pythonw.exe not on PATH).
            // Fall back to UseShellExecute = true which at least avoids cmd.exe.
            _logger.LogWarning(
                "CreateProcess for pythonw.exe failed (error {Err}); falling back to ShellExecute.",
                Marshal.GetLastWin32Error());

            var psi = new ProcessStartInfo
            {
                FileName        = pythonw,
                Arguments       = args,
                UseShellExecute = true,
                WindowStyle     = ProcessWindowStyle.Hidden,
            };
            if (entry.RunAsAdmin) psi.Verb = "runas";
            Process.Start(psi);
            return;
        }

        // Close the returned handles immediately — we don't track the child.
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);

        _logger.LogInformation(
            "Python script launched detached (pid {Pid}): {Path}", pi.dwProcessId, entry.Path);
    }

    private static ProcessStartInfo BuildVBScript(AppEntry e)
        => new ProcessStartInfo
        {
            FileName = "wscript.exe",
            Arguments = "\"" + e.Path + "\" " + e.Arguments
        };

    private static ProcessStartInfo BuildShellCommand(AppEntry e)
        => new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + e.Path + " " + e.Arguments
        };

    private static ProcessStartInfo BuildDocument(AppEntry e)
        => new ProcessStartInfo { FileName = e.Path };

    private static ProcessStartInfo BuildNetworkShare(AppEntry e)
        => new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + e.Path + "\"" };

    private static ProcessStartInfo BuildSsh(AppEntry e)
        => new ProcessStartInfo { FileName = "ssh.exe", Arguments = e.Path + " " + e.Arguments };

    private static ProcessStartInfo BuildWsl(AppEntry e)
        => new ProcessStartInfo { FileName = "wsl.exe", Arguments = e.Path + " " + e.Arguments };

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string GetTypeLabel(AppEntryType type)
    {
        if (_typeLabels.TryGetValue(type, out var label))
            return label;
        return type.ToString();
    }

    /// <inheritdoc/>
    public IReadOnlyList<AppEntryType> GetSupportedTypes()
    {
        var list = new List<AppEntryType>(Enum.GetValues<AppEntryType>());
        return list.AsReadOnly();
    }
}
