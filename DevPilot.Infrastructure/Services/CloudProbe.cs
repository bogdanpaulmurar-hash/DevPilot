using System.Diagnostics;
using DevPilot.Core.Cloud;

namespace DevPilot.Infrastructure.Services;

/// <summary>Process list and file checks only; no directory scans.</summary>
public sealed class CloudProbe : ICloudProbe
{
    public bool IsProcessRunning(string processName)
    {
        try
        {
            var found = Process.GetProcessesByName(processName);
            foreach (var p in found) p.Dispose();
            return found.Length > 0;
        }
        catch (Exception) { return false; }
    }

    public bool IsRootMounted(string root)
    {
        try { return Directory.Exists(root); }
        catch (Exception) { return false; }
    }

    public bool IsReachable(string path)
    {
        try { return Directory.Exists(path) || File.Exists(path); }
        catch (Exception) { return false; }
    }
}
