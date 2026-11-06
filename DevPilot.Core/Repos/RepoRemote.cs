namespace DevPilot.Core.Repos;

/// <summary>Remote addresses of a repository: finds and removes a user name (or password) written into an https address.</summary>
public static class RepoRemote
{
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
