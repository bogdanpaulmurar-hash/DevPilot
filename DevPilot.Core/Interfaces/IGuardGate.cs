using DevPilot.Core.Guard;
using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

/// <summary>Asked by the launcher before it starts a tool.</summary>
public interface IGuardGate
{
    Task<GateDecision> CheckAsync(ToolEntry tool);
}
