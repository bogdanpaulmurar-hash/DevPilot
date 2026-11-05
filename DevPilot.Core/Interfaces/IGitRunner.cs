namespace DevPilot.Core.Interfaces;

/// <summary>Result of one git command. <see cref="GitMissing"/>: git.exe was not found.</summary>
public sealed record GitRun(int ExitCode, string StdOut, string StdErr, bool TimedOut = false, bool GitMissing = false);

/// <summary>Runs git in a folder without a console window and without interactive prompts.</summary>
public interface IGitRunner
{
    Task<GitRun> RunAsync(string workingDirectory, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct = default);
}
