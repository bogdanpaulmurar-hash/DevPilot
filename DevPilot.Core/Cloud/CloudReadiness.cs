using DevPilot.Models;

namespace DevPilot.Core.Cloud;

/// <summary>Which cloud drive a path lives on and whether that drive is connected. Pure logic over <see cref="ICloudProbe"/>.</summary>
public static class CloudReadiness
{
    public static string ProcessName(CloudProvider provider) => provider switch
    {
        CloudProvider.OneDrive => "OneDrive",
        CloudProvider.GoogleDrive => "GoogleDriveFS",
        _ => string.Empty,
    };

    public static string DisplayName(CloudProvider provider) => provider switch
    {
        CloudProvider.OneDrive => "OneDrive",
        CloudProvider.GoogleDrive => "Google Drive",
        _ => "cloud drive",
    };

    /// <summary>
    /// Provider for a path: the explicit choice if there is one, otherwise guessed from the path (a "OneDrive…" folder,
    /// "Google Drive" / "My Drive" / "Shared drives"). <see cref="CloudProvider.Auto"/> comes back when the path is not on a cloud drive.
    /// </summary>
    public static CloudProvider Resolve(CloudProvider chosen, string path, IEnumerable<string>? oneDriveRoots = null)
    {
        if (chosen != CloudProvider.Auto) return chosen;
        if (string.IsNullOrWhiteSpace(path)) return CloudProvider.Auto;
        var full = path.Trim().Trim('"');

        foreach (var root in oneDriveRoots ?? [])
            if (!string.IsNullOrWhiteSpace(root) && IsUnder(full, root)) return CloudProvider.OneDrive;

        foreach (var segment in full.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase)) return CloudProvider.OneDrive;
            if (segment.Equals("My Drive", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("Shared drives", StringComparison.OrdinalIgnoreCase)
                || segment.StartsWith("Google Drive", StringComparison.OrdinalIgnoreCase)
                || segment.StartsWith("GoogleDrive", StringComparison.OrdinalIgnoreCase)) return CloudProvider.GoogleDrive;
        }
        return CloudProvider.Auto;
    }

    /// <summary>Ready = the target is reachable and the provider's process runs (Google Drive: its drive is also mounted).</summary>
    public static bool IsReady(CloudProvider provider, string path, ICloudProbe probe)
    {
        if (provider == CloudProvider.Auto) return true;
        if (!probe.IsProcessRunning(ProcessName(provider))) return false;
        if (provider == CloudProvider.GoogleDrive)
        {
            var root = RootOf(path);
            if (root.Length > 0 && !probe.IsRootMounted(root)) return false;
        }
        return probe.IsReachable(path);
    }

    internal static string RootOf(string path)
    {
        try { return Path.GetPathRoot(path.Trim().Trim('"')) ?? string.Empty; }
        catch (ArgumentException) { return string.Empty; }
    }

    private static bool IsUnder(string path, string root)
    {
        var r = root.Trim().TrimEnd('\\', '/');
        return path.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase)
            || path.Equals(r, StringComparison.OrdinalIgnoreCase);
    }
}
