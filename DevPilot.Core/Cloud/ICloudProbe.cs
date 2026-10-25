namespace DevPilot.Core.Cloud;

/// <summary>The few cheap facts about the machine that cloud readiness is built from (fake in tests).</summary>
public interface ICloudProbe
{
    /// <summary>A process with this name (no extension) is running.</summary>
    bool IsProcessRunning(string processName);
    /// <summary>The drive or share root exists, e.g. "G:\".</summary>
    bool IsRootMounted(string root);
    /// <summary>The file or folder exists and can be reached. No directory scan.</summary>
    bool IsReachable(string path);
}
