using System.Diagnostics;
using System.Management;
using DevPilot.Core.Launch;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// Fast process snapshot: names for every process, command lines only for script hosts
/// (python, wscript, powershell, cmd …) where the name alone does not identify the tool.
/// </summary>
public static class ProcessSnapshot
{
    private static readonly string[] ScriptHosts =
        ["python.exe", "pythonw.exe", "py.exe", "pyw.exe", "wscript.exe", "cscript.exe", "powershell.exe", "pwsh.exe", "cmd.exe"];

    public static List<ProcessInfo> Take()
    {
        var result = new List<ProcessInfo>(400);
        var commandLines = QueryScriptHostCommandLines();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                string name;
                try { name = p.ProcessName + ".exe"; } catch (InvalidOperationException) { continue; }
                commandLines.TryGetValue(p.Id, out var cmd);
                result.Add(new ProcessInfo(p.Id, name, cmd ?? string.Empty));
            }
        }
        return result;
    }

    private static Dictionary<int, string> QueryScriptHostCommandLines()
    {
        var map = new Dictionary<int, string>();
        var where = string.Join(" OR ", ScriptHosts.Select(h => $"Name='{h}'"));
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT ProcessId, CommandLine FROM Win32_Process WHERE {where}");
            using var results = searcher.Get();
            foreach (ManagementObject mo in results)
            {
                using (mo)
                {
                    var id = Convert.ToInt32(mo["ProcessId"]);
                    map[id] = mo["CommandLine"] as string ?? string.Empty;
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // WMI unavailable: name-based matching still works.
        }
        return map;
    }
}
