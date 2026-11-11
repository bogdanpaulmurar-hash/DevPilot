using System.ComponentModel;
using System.Diagnostics;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Repos;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// A visible cmd.exe window that DevPilot waits for. Nothing is redirected and prompts stay on, so git and Git
/// Credential Manager can ask for a sign-in there.
/// </summary>
public sealed class SignInWindow : ISignInWindow
{
    public async Task<WindowRun> RunAsync(string workingDirectory, string commandLine, TimeSpan timeout, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.SystemDirectory,
            // /s removes exactly the outer quotes, so the quotes inside the line are kept.
            Arguments = $"/d /s /c \"{commandLine.Trim()}\"",
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        psi.Environment.Remove("DEVPILOT_MANAGED");
        psi.Environment.Remove("DEVPILOT_SUPPRESS");
        psi.Environment.Remove(RepoSignIn.FailedMark);
        // The whole point of the window: git may ask here, whatever DevPilot itself was started with.
        psi.Environment.Remove("GIT_TERMINAL_PROMPT");
        psi.Environment.Remove("GCM_INTERACTIVE");

        using var proc = new Process { StartInfo = psi };
        try { proc.Start(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { return new WindowRun(-1, Error: ex.Message); }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try { await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            // The window is the user's: leave it open, just stop waiting for it.
            ct.ThrowIfCancellationRequested();
            return new WindowRun(-1, TimedOut: true);
        }
        return new WindowRun(proc.ExitCode);
    }
}
