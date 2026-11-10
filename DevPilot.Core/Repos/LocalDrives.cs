namespace DevPilot.Core.Repos;

/// <summary>A drive of this PC that could hold a local clone.</summary>
public sealed record DriveCandidate(string Root, long FreeBytes);

/// <summary>One place to offer for a local clone: the drive and the folder the clone would get.</summary>
public sealed record DriveChoice(string Root, long FreeBytes, string Path)
{
    /// <summary>"C:" for the menu.</summary>
    public string Letter => Root.TrimEnd('\\', '/');
    public string FreeText => FreeBytes <= 0 ? string.Empty : $"{FreeBytes / (1024.0 * 1024 * 1024):0} GB free";
}

/// <summary>
/// Where a local clone can go on this PC. Drives differ between laptops, so they are read at run time. C and D come
/// first; the drive that holds the cloud copy (usually the synced drive) is left out. DevPilot only proposes: the user
/// picks the drive before anything is cloned.
/// </summary>
public static class LocalDrives
{
    /// <summary>Folder on the chosen drive that holds the clones: <c>D:\Dev\ScreenPilot</c>.</summary>
    public const string ParentFolder = "Dev";

    /// <summary>Fixed, ready drives with their free space; empty when they cannot be read.</summary>
    public static IReadOnlyList<DriveCandidate> Detect()
    {
        var list = new List<DriveCandidate>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    list.Add(new DriveCandidate(d.Name, d.AvailableFreeSpace));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list;
    }

    /// <summary>
    /// The drives to offer for <paramref name="repoName"/>: not the drive of the cloud copy, C then D then the others by
    /// letter, and only where the clone folder is free (missing or empty).
    /// </summary>
    public static IReadOnlyList<DriveChoice> Choices(string repoName, string cloudPath, IEnumerable<DriveCandidate> drives)
    {
        var name = string.IsNullOrWhiteSpace(repoName) ? "repository" : repoName.Trim();
        var cloudRoot = SafeRoot(cloudPath);
        return drives
            .Where(d => !string.IsNullOrWhiteSpace(d.Root))
            .Where(d => !string.Equals(SafeRoot(d.Root), cloudRoot, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => Rank(d.Root)).ThenBy(d => d.Root, StringComparer.OrdinalIgnoreCase)
            .Select(d => new DriveChoice(d.Root, d.FreeBytes, Path.Combine(d.Root, ParentFolder, name)))
            .Where(c => RepoCopies.CanClone(c.Path))
            .ToList();
    }

    private static int Rank(string root) => char.ToUpperInvariant(root.TrimStart()[0]) switch { 'C' => 0, 'D' => 1, _ => 2 };

    private static string SafeRoot(string path)
    {
        try { return Path.GetPathRoot(Path.GetFullPath(path)) ?? string.Empty; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return string.Empty; }
    }
}
