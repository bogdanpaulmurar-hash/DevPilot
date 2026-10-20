using DevPilot.Models;

namespace DevPilot.Core.Folders;

public sealed record FolderItem(string Name, string FullPath, bool IsDirectory, DateTime Modified, long Size);

public sealed class FolderListing
{
    public IReadOnlyList<FolderItem> Items { get; init; } = [];
    /// <summary>Number of matching entries that were not returned because of <see cref="FolderOptions.MaxItems"/>.</summary>
    public int Truncated { get; init; }
    public string? Error { get; init; }
}

/// <summary>Lists the contents of a reference folder for the hub (one level at a time; nesting is lazy).</summary>
public static class FolderBrowser
{
    public static FolderListing List(string path, FolderOptions options)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) return new FolderListing { Error = "Folder not found" };

            var patterns = (options.Filter ?? string.Empty)
                .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var enumOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = options.ShowHidden ? FileAttributes.None : FileAttributes.Hidden | FileAttributes.System,
                RecurseSubdirectories = false,
            };

            var dirs = options.ShowFolders
                ? dir.EnumerateDirectories("*", enumOptions)
                    .Select(d => new FolderItem(d.Name, d.FullName, true, d.LastWriteTime, 0))
                : [];
            var files = options.ShowFiles
                ? dir.EnumerateFiles("*", enumOptions)
                    .Where(f => patterns.Length == 0 || patterns.Any(p => WildcardMatch(f.Name, p)))
                    .Select(f => new FolderItem(f.Name, f.FullName, false, f.LastWriteTime, f.Length))
                : [];

            var dirList = dirs.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var fileList = files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var all = options.FoldersFirst
                ? dirList.Concat(fileList).ToList()
                : dirList.Concat(fileList).OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

            int max = Math.Max(1, options.MaxItems);
            return new FolderListing
            {
                Items = all.Take(max).ToList(),
                Truncated = Math.Max(0, all.Count - max),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return new FolderListing { Error = ex.Message };
        }
    }

    /// <summary>Simple * and ? matching, case-insensitive.</summary>
    public static bool WildcardMatch(string text, string pattern)
    {
        int t = 0, p = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(text[t])))
            { t++; p++; }
            else if (p < pattern.Length && pattern[p] == '*')
            { star = p++; mark = t; }
            else if (star >= 0)
            { p = star + 1; t = ++mark; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    /// <summary>Most recent files of a folder (ScreenPilot spotlight).</summary>
    public static IReadOnlyList<FolderItem> RecentFiles(string path, int count, params string[] extensions)
    {
        if (count <= 0 || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return [];
        try
        {
            var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, MaxRecursionDepth = 2 };
            return new DirectoryInfo(path).EnumerateFiles("*", opts)
                .Where(f => extensions.Length == 0 || extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(count)
                .Select(f => new FolderItem(f.Name, f.FullName, false, f.LastWriteTime, f.Length))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
