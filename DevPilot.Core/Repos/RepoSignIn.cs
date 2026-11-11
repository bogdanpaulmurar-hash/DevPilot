using System.Text.RegularExpressions;

namespace DevPilot.Core.Repos;

public enum RepoSignInOutcome
{
    /// <summary>Every step in the window ended well: git can reach the remote (and a missing local copy is cloned).</summary>
    SignedIn,
    /// <summary>The window ran, but a step failed (the window stayed open so the reason could be read).</summary>
    Failed,
    /// <summary>Something else is running on the repository.</summary>
    Busy,
    CouldNotStart,
    TimedOut,
}

public sealed record RepoSignInResult(RepoSignInOutcome Outcome, string Detail = "");

/// <summary>
/// The sign-in fix: a visible console window where git may ask (Git Credential Manager opens its window or a browser
/// page). It runs <c>git fetch</c> in each existing copy, which signs in without touching any file, and clones a local
/// copy that is still missing, so the step that failed in the background is done where the user can answer.
/// </summary>
public static partial class RepoSignIn
{
    /// <summary>Set inside the window when a step fails; decides its exit code.</summary>
    public const string FailedMark = "DEVPILOT_SIGNIN_FAILED";
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

    /// <summary><c>git -C "copy" fetch</c>: contacts the remote (so git signs in) and changes no file of the working tree.</summary>
    public static string Fetch(string git, string copy) => $"{Exe(git)} -C {Quote(copy)} fetch";

    /// <summary><c>git clone --branch b -- "url" "target"</c>, the same clone the background run does.</summary>
    public static string Clone(string git, string url, string branch, string target) =>
        Exe(git) + " clone" + (string.IsNullOrWhiteSpace(branch) ? string.Empty : " --branch " + Quote(branch.Trim())) + $" -- {Quote(url)} {Quote(target)}";

    /// <summary>
    /// One cmd.exe line: every command runs even when an earlier one failed. All fine: the window closes by itself with
    /// exit code 0. Otherwise it stays open until a key is pressed and ends with exit code 1.
    /// </summary>
    public static string Script(IReadOnlyList<string> commands)
    {
        var steps = string.Join(" & ", commands.Select(c => $"({c} || set {FailedMark}=1)"));
        return "echo DevPilot: git sign-in. If a window or a browser page asks, sign in there. & echo. & " + steps +
               $" & if defined {FailedMark} (echo. & echo Something did not work. Read the lines above, then press a key to close. & pause >nul & exit 1)" +
               " else (echo. & echo Done. This window closes by itself. & timeout /t 3 /nobreak >nul & exit 0)";
    }

    /// <summary>
    /// Quoted for cmd.exe and git.exe. A quote cannot be part of a Windows path and is dropped; a trailing backslash is
    /// doubled so it does not swallow the closing quote.
    /// </summary>
    public static string Quote(string value)
    {
        var v = (value ?? string.Empty).Replace("\"", string.Empty);
        if (v.EndsWith('\\')) v += "\\";
        return "\"" + v + "\"";
    }

    private static string Exe(string git) => string.IsNullOrWhiteSpace(git) ? "git" : git.Contains(' ') ? Quote(git) : git;

    /// <summary>Removes a user name or token written into an address ("https://user:secret@host/"), for logs.</summary>
    public static string Redact(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : UserInfo().Replace(text, "$1");

    [GeneratedRegex(@"(\b[a-z][a-z0-9+.-]*://)[^/\s@]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UserInfo();
}
