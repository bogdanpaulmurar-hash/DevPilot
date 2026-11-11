using System.Text.RegularExpressions;

namespace DevPilot.Core.Repos;

/// <summary>
/// Remote addresses of a repository: finds and removes a user name (or password) written into an https address, and
/// tells which account a repository belongs to and which account git tried to sign in with.
/// </summary>
public static partial class RepoRemote
{
    /// <summary>
    /// "https://host/owner/repo.git" gives ("https://host", "owner"): the site and the account the repository belongs
    /// to. Null for an address that is not http(s) or has no owner and repository part.
    /// </summary>
    public static (string Site, string Owner)? Account(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = StripUser(url) ?? url.Trim();
        var m = OwnerPattern().Match(url);
        return m.Success ? (m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value) : null;
    }

    /// <summary>
    /// The account git tried when it gave up asking for a password ("could not read Password for
    /// 'https://account@host'"): a user name set in the git configuration of the PC. Null when the output names none.
    /// </summary>
    public static string? AttemptedUser(string? output)
    {
        if (string.IsNullOrEmpty(output)) return null;
        var m = AttemptPattern().Match(output);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>The git setting that names the account for a site: <c>credential.https://host.username</c>.</summary>
    public static string UsernameKey(string site) => $"credential.{site}.username";

    // Owner and repository: the characters hosting sites allow in names, so the values are safe on a command line.
    [GeneratedRegex(@"^(https?://[A-Za-z0-9.-]+(?::\d+)?)/([A-Za-z0-9][A-Za-z0-9._-]*)/[A-Za-z0-9._-]+?(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex OwnerPattern();

    [GeneratedRegex(@"could not read (?:Password|Username) for '[a-z][a-z0-9+.-]*://([A-Za-z0-9][A-Za-z0-9._-]*)(?::[^@'/]*)?@", RegexOptions.IgnoreCase)]
    private static partial Regex AttemptPattern();

    /// <summary>
    /// "https://user@host/path" (or "user:secret@") becomes "https://host/path", so git uses the sign-in saved for the
    /// site instead of asking for that user's password. Null when the address has no user part or is not http(s).
    /// </summary>
    public static string? StripUser(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        int scheme = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? 8
                   : url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? 7 : 0;
        if (scheme == 0) return null;
        int slash = url.IndexOf('/', scheme);
        string authority = slash < 0 ? url[scheme..] : url[scheme..slash];
        int at = authority.LastIndexOf('@');
        if (at < 0) return null;
        return url[..scheme] + authority[(at + 1)..] + (slash < 0 ? string.Empty : url[slash..]);
    }
}
