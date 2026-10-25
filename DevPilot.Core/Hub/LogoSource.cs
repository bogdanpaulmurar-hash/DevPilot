using System.Security.Cryptography;
using System.Text;
using DevPilot.Core.Launch;
using DevPilot.Models;

namespace DevPilot.Core.Hub;

/// <summary>Where a row's logo comes from.</summary>
public enum LogoOrigin
{
    None,
    /// <summary>The file the user picked (<see cref="ToolEntry.IconPath"/>).</summary>
    File,
    /// <summary>The "icon" of the tool's devpilot.tool.json.</summary>
    Manifest,
    /// <summary>An icon.ico / logo.ico found next to the tool by its conventional name.</summary>
    Convention,
    /// <summary>The icon of the program or document the entry opens.</summary>
    Target,
}

public readonly record struct LogoRef(string Path, LogoOrigin Origin)
{
    public static readonly LogoRef None = new(string.Empty, LogoOrigin.None);
    public bool IsNone => Origin == LogoOrigin.None;
}

/// <summary>
/// Picks the logo file for an entry and builds the cache key for it. Priority: the user's file, then the manifest's
/// "icon", then the icon of what the entry opens (programs and documents only: scripts, folders and links would all
/// get the same generic icon, which is no logo at all). Only paths are handled here; reading images is the UI's job.
/// </summary>
public static class LogoSource
{
    /// <summary>Logo size kept in the cache, in pixels. Large enough to stay sharp at 200% display scaling.</summary>
    public const int CachePixels = 64;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    private static readonly HashSet<string> IconHolderExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".dll", ".lnk", ".url" };

    public static bool IsImage(string path) => ImageExtensions.Contains(Ext(path));
    public static bool IsIcoFile(string path) => Ext(path).Equals(".ico", StringComparison.OrdinalIgnoreCase);
    /// <summary>A file the icon has to be extracted from (program, library, shortcut).</summary>
    public static bool IsIconHolder(string path) => IconHolderExtensions.Contains(Ext(path));

    /// <summary>Names looked up (no folder scan) in the tool's folder, in this order.</summary>
    public static readonly string[] ConventionalNames =
        [@"icon.ico", @"logo.ico", @"assets\icon.ico", @"assets\logo.ico"];

    /// <summary>"Logo from: …" text for the Tools page.</summary>
    public static string Describe(LogoOrigin origin) => origin switch
    {
        LogoOrigin.File => "the file you picked",
        LogoOrigin.Manifest => "devpilot.tool.json",
        LogoOrigin.Convention => "icon.ico next to the tool",
        LogoOrigin.Target => "the program's own icon",
        _ => "none (symbol is used)",
    };

    /// <param name="manifestIcon">The manifest's icon as an absolute path (see <see cref="ManifestIconPath"/>), or null.</param>
    /// <param name="fileExists">Checks a conventional icon path; null skips that source.</param>
    public static LogoRef Resolve(ToolEntry tool, string? manifestIcon, Func<string, bool>? fileExists = null)
    {
        if (tool.IsSeparator) return LogoRef.None;

        var own = Clean(PathTokens.Expand(tool.IconPath, tool));
        if (own.Length > 0) return new(own, LogoOrigin.File);

        var fromManifest = Clean(manifestIcon);
        if (fromManifest.Length > 0) return new(fromManifest, LogoOrigin.Manifest);

        if (fileExists is not null)
        {
            var dir = PathTokens.ToolDirectory(tool);
            if (dir.Length > 0)
                foreach (var name in ConventionalNames)
                {
                    var candidate = Path.Combine(dir, name);
                    if (fileExists(candidate)) return new(candidate, LogoOrigin.Convention);
                }
        }

        var target = Clean(PathTokens.Expand(tool.Path, tool));
        if (target.Length == 0) return LogoRef.None;
        return tool.Kind switch
        {
            ToolKind.Executable when IsIconHolder(target) => new(target, LogoOrigin.Target),
            ToolKind.Document => new(target, LogoOrigin.Target),
            _ => LogoRef.None,
        };
    }

    /// <summary>The manifest's "icon" as an absolute path: relative paths are taken from the manifest's folder.</summary>
    public static string ManifestIconPath(string manifestPath, string? icon)
    {
        var value = Clean(icon is null ? null : Environment.ExpandEnvironmentVariables(icon));
        if (value.Length == 0) return string.Empty;
        try
        {
            if (Path.IsPathRooted(value)) return Path.GetFullPath(value);
            var dir = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(dir, value));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// File name (without folder) of the cached PNG. Changes whenever the source file changes (size or date), so a
    /// new logo is picked up without clearing anything.
    /// </summary>
    public static string CacheKey(string path, long length, DateTime lastWriteUtc, int pixels = CachePixels)
    {
        var text = $"{path.Trim().ToUpperInvariant()}|{length}|{lastWriteUtc.Ticks}|{pixels}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash, 0, 10).ToLowerInvariant() + ".png";
    }

    /// <summary>Fits a w×h image into a square of <paramref name="size"/> pixels, keeping its proportions.</summary>
    public static (int Width, int Height) FitInto(int width, int height, int size = CachePixels)
    {
        if (width <= 0 || height <= 0) return (size, size);
        var scale = Math.Min((double)size / width, (double)size / height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static string Clean(string? path) => (path ?? string.Empty).Trim().Trim('"').Trim();

    private static string Ext(string path)
    {
        try { return Path.GetExtension(Clean(path)); }
        catch (ArgumentException) { return string.Empty; }
    }
}
