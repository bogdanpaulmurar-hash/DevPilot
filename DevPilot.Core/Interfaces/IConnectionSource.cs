using DevPilot.Core.Guard;

namespace DevPilot.Core.Interfaces;

/// <summary>Where the runtime monitor reads the machine's outbound connections from.</summary>
public interface IConnectionSource
{
    /// <summary>Established or connecting TCP connections of all processes.</summary>
    IReadOnlyList<ConnectionInfo> Snapshot();
}
