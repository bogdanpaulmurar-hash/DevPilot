using DevPilot.Core.Startup;
using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

public interface IStartupManager
{
    bool IsDevPilotRegistered { get; }
    void SetDevPilotStartup(bool enable);
    IReadOnlyList<StartupEntry> ScanEntries();
    IReadOnlyList<StartupConflict> FindConflicts();
    StartupTakeover TakeOver(StartupConflict conflict);
    void Restore(StartupTakeover takeover);
    /// <summary>Removes entries a managed tool has re-created. Returns how many were removed again.</summary>
    int EnforceTakeovers();
}
