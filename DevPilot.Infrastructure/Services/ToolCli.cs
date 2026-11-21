using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using DevPilot.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DevPilot.Infrastructure.Services;

/// <summary>Runs a tool's program hidden, with UTF-8 output captured (stdout and stderr together), and waits for it.</summary>
public sealed class ToolCli : IToolCli
{
    private const int MaxChars = 64 * 1024;
    private readonly ILogger<ToolCli> _logger;

    public ToolCli(ILogger<ToolCli> logger) => _logger = logger;

    public async Task<ToolCliResult> RunAsync(string program, string arguments, TimeSpan timeout, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(program) || !File.Exists(program))
            return new ToolCliResult(-1, string.Empty, StartError: $"Program not found: {program}");

        var psi = new ProcessStartInfo(program, arguments)
        {
            WorkingDirectory = Path.GetDirectoryName(program) ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        // A command-line call is not a managed start: DevPilot's markers must not leak into it.
        psi.Environment.Remove("DEVPILOT_MANAGED");
        psi.Environment.Remove("DEVPILOT_SUPPRESS");

        using var proc = new Process { StartInfo = psi };
        var buffer = new StringBuilder();
        var sync = new object();
        void Append(string? line)
        {
            if (line is null) return;
            lock (sync) { if (buffer.Length < MaxChars) buffer.AppendLine(line); }
        }
        proc.OutputDataReceived += (_, e) => Append(e.Data);
        proc.ErrorDataReceived += (_, e) => Append(e.Data);
        try { proc.Start(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new ToolCliResult(-1, string.Empty, StartError: ex.Message);
        }
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            _logger.LogWarning("{Program} {Args} timed out", Path.GetFileName(program), arguments);
            string partial;
            lock (sync) partial = buffer.ToString();
            return new ToolCliResult(-1, partial, TimedOut: true);
        }
        proc.WaitForExit(); // flushes the redirected streams
        string output;
        lock (sync) output = buffer.ToString().Trim();
        _logger.LogInformation("{Program} {Args}: exit {Code}", Path.GetFileName(program), arguments, proc.ExitCode);
        return new ToolCliResult(proc.ExitCode, output);
    }
}
