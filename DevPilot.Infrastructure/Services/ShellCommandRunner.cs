using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Repos;
using DevPilot.Models;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// Runs a repository command through cmd.exe. Hidden mode has no window and captures the output
/// (stdout and stderr together, only the end is kept); Window mode opens a console that stays open (<c>/k</c>)
/// and is not waited for.
/// </summary>
public sealed class ShellCommandRunner : ICommandRunner
{
    private const int KeepChars = RepoCommandRunner.MaxOutputChars * 2;

    public async Task<CommandRun> RunAsync(RepoCommand command, string workingDirectory, TimeSpan timeout, CancellationToken ct = default)
    {
        if (!Directory.Exists(workingDirectory))
            return new CommandRun(-1, string.Empty, StartError: "The repository folder does not exist.");

        bool window = command.Mode == RepoCommandMode.Window;
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            WorkingDirectory = workingDirectory,
            Arguments = RepoCommandRunner.Arguments(command),
            UseShellExecute = false,
            CreateNoWindow = !window,
        };
        // A command started by DevPilot is the user's own, not a managed tool: DevPilot's markers must not leak into it.
        psi.Environment.Remove("DEVPILOT_MANAGED");
        psi.Environment.Remove("DEVPILOT_SUPPRESS");
        if (!window)
        {
            // Nobody can answer a prompt in a hidden command: a sign-in, a passphrase or a merge-message editor would
            // wait until the timeout. Fail at once instead (use "In a window" for a command that needs input).
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GCM_INTERACTIVE"] = "never";
            psi.Environment["GIT_MERGE_AUTOEDIT"] = "no";
            psi.Environment["GIT_EDITOR"] = "true";
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GIT_SSH_COMMAND")))
                psi.Environment["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes";
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.StandardOutputEncoding = OemEncoding();
            psi.StandardErrorEncoding = psi.StandardOutputEncoding;
        }

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var buffer = new StringBuilder();
        var sync = new object();
        void Append(string? line)
        {
            if (line is null) return;
            lock (sync)
            {
                buffer.AppendLine(line);
                if (buffer.Length > KeepChars * 2) buffer.Remove(0, buffer.Length - KeepChars);
            }
        }
        try
        {
            if (!window)
            {
                proc.OutputDataReceived += (_, e) => Append(e.Data);
                proc.ErrorDataReceived += (_, e) => Append(e.Data);
            }
            proc.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new CommandRun(-1, string.Empty, StartError: ex.Message);
        }
        if (window) return new CommandRun(0, string.Empty, Started: true);

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        // The command must never wait for keyboard input that nobody can type.
        try { proc.StandardInput.Close(); } catch (IOException) { }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            proc.WaitForExit(); // flush the output events
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (Win32Exception) { }
            ct.ThrowIfCancellationRequested();
            string partial; lock (sync) partial = buffer.ToString();
            return new CommandRun(-1, partial, TimedOut: true);
        }
        string output; lock (sync) output = buffer.ToString();
        return new CommandRun(proc.ExitCode, output);
    }

    /// <summary>cmd.exe writes in the console (OEM) code page; falls back to UTF-8 if that page is not available.</summary>
    private static Encoding OemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return Encoding.UTF8; }
    }
}
