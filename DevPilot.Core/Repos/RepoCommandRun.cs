using DevPilot.Core.Interfaces;
using DevPilot.Models;

namespace DevPilot.Core.Repos;

public enum RepoCommandStatus
{
    /// <summary>Finished with exit code 0.</summary>
    Succeeded,
    /// <summary>Finished with a non-zero exit code.</summary>
    Failed,
    TimedOut,
    /// <summary>cmd.exe could not be started.</summary>
    StartFailed,
    /// <summary>Opened in its own console window; DevPilot does not wait for it.</summary>
    Started,
    /// <summary>Another action is already running on this repository.</summary>
    Busy,
    /// <summary>Nothing was done on purpose (for example an update stopped by uncommitted changes).</summary>
    Skipped,
}

public sealed record RepoCommandResult(string Path, string Name, RepoCommandStatus Status, int ExitCode, string OutputTail, DateTime At, bool ShowAllLines = false)
{
    public bool IsOk => Status is RepoCommandStatus.Succeeded or RepoCommandStatus.Started;

    public string StatusText => Status switch
    {
        RepoCommandStatus.Succeeded => "Done",
        RepoCommandStatus.Failed => $"Failed (exit code {ExitCode})",
        RepoCommandStatus.TimedOut => "Timed out",
        RepoCommandStatus.StartFailed => "Could not start",
        RepoCommandStatus.Started => "Opened in a window",
        RepoCommandStatus.Skipped => "Skipped",
        _ => "Busy",
    };

    /// <summary>One short line for a row or a notification: the last line of output, or the status.</summary>
    public string Message
    {
        get
        {
            var last = RepoCommandRunner.LastLines(OutputTail, 1);
            return last.Length > 0 ? last : StatusText;
        }
    }
}

/// <summary>Builds the cmd.exe call for a repository command and turns its outcome into a result.</summary>
public static class RepoCommandRunner
{
    public const int MaxOutputChars = 8000;
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The arguments for cmd.exe. Hidden: <c>/d /s /c "line"</c> (runs and exits). Window: <c>/d /s /k "line"</c>
    /// (the window stays open, so an error can be read). <c>/s</c> removes exactly the outer quotes, so quotes inside the line are kept.
    /// </summary>
    public static string Arguments(RepoCommand c) =>
        $"/d /s {(c.Mode == RepoCommandMode.Window ? "/k" : "/c")} \"{c.CommandLine.Trim()}\"";

    public static RepoCommandResult ToResult(string repo, string name, CommandRun run, DateTime at)
    {
        var tail = Tail(run.Output);
        if (run.StartError is not null) return new(repo, name, RepoCommandStatus.StartFailed, -1, run.StartError, at);
        if (run.Started) return new(repo, name, RepoCommandStatus.Started, 0, string.Empty, at);
        if (run.TimedOut) return new(repo, name, RepoCommandStatus.TimedOut, -1, tail, at);
        return new(repo, name, run.ExitCode == 0 ? RepoCommandStatus.Succeeded : RepoCommandStatus.Failed, run.ExitCode, tail, at);
    }

    /// <summary>Keeps the end of the output (where errors are), cut at a line start when possible.</summary>
    public static string Tail(string? output, int max = MaxOutputChars)
    {
        if (string.IsNullOrEmpty(output)) return string.Empty;
        var text = output.Replace("\r\n", "\n").TrimEnd();
        if (text.Length <= max) return text;
        text = text[^max..];
        int nl = text.IndexOf('\n');
        return nl >= 0 && nl < text.Length - 1 ? text[(nl + 1)..] : text;
    }

    /// <summary>The last <paramref name="count"/> non-empty lines.</summary>
    public static string LastLines(string? output, int count)
    {
        if (string.IsNullOrWhiteSpace(output)) return string.Empty;
        var lines = output.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
        return string.Join('\n', lines.Skip(Math.Max(0, lines.Count - count)));
    }

    /// <summary>"3 done · 1 failed" for several commands.</summary>
    public static string Summary(IReadOnlyCollection<RepoCommandResult> results)
    {
        int ok = results.Count(r => r.IsOk);
        int bad = results.Count - ok;
        var parts = new List<string>();
        if (ok > 0) parts.Add($"{ok} done");
        if (bad > 0) parts.Add($"{bad} failed");
        return parts.Count == 0 ? "Nothing to run." : string.Join(" · ", parts);
    }
}
