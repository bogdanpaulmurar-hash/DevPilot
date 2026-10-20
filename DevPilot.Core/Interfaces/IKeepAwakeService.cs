using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

public interface IKeepAwakeService : IDisposable
{
    /// <summary>True while a power request is held (enabled and inside the schedule).</summary>
    bool IsActive { get; }
    string StatusText { get; }
    event EventHandler? StateChanged;
    void Apply(KeepAwakeSettings settings);
}
