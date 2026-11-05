using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using DevPilot.Core.Interfaces;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// Runs git.exe hidden, with English output (so results can be classified) and terminal prompts off,
/// so a missing sign-in fails instead of waiting forever. Git Credential Manager can still show its own window.
/// </summary>
public sealed class GitRunner : IGitRunner
{
    private static string? _git;

    /// <summary>git on PATH, else the usual Git for Windows locations.</summary>
    public static string GitPath => _git ??= Locate();

    private static string Locate()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"),
        ];
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            try
            {
                var p = Path.Combine(dir.Trim('"'), OperatingSystem.IsWindows() ? "git.exe" : "git");
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return candidates.FirstOrDefault(File.Exists) ?? "git";
    }

    public async Task<GitRun> RunAsync(string workingDirectory, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(GitPath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["LANG"] = "C";
        // DevPilot's own markers must not leak into git hooks or credential helpers.
        psi.Environment.Remove("DEVPILOT_MANAGED");
        psi.Environment.Remove("DEVPILOT_SUPPRESS");

        using var proc = new Process { StartInfo = psi };
        try { proc.Start(); }
        catch (Win32Exception) { return new GitRun(-1, string.Empty, string.Empty, GitMissing: true); }

        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (Win32Exception) { }
            ct.ThrowIfCancellationRequested();
            return new GitRun(-1, string.Empty, string.Empty, TimedOut: true);
        }
        string o = string.Empty, e = string.Empty;
        try { o = await stdout.ConfigureAwait(false); e = await stderr.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        return new GitRun(proc.ExitCode, o, e);
    }
}
