using DevPilot.Core.Guard;
using DevPilot.Core.Interfaces;
using DevPilot.Models;
using Microsoft.Extensions.Logging;

namespace DevPilot.Infrastructure.Services;

/// <summary>Applies <see cref="ApprovalGate"/> with the current guard mode; hashing runs off the UI thread.</summary>
public sealed class GuardGate : IGuardGate
{
    private readonly IConfigStore _store;
    private readonly ILogger<GuardGate> _logger;

    public GuardGate(IConfigStore store, ILogger<GuardGate> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task<GateDecision> CheckAsync(ToolEntry tool)
    {
        var mode = _store.Current.Guard.Mode;
        if (mode == EgressMode.Off || !ApprovalGate.Applies(tool)) return Task.FromResult(GateDecision.Skipped);
        return Task.Run(() =>
        {
            try
            {
                var decision = ApprovalGate.Evaluate(mode, tool, () => ProvenanceReader.ReadForTool(tool));
                _logger.LogInformation("Egress Guard ({Mode}) {Tool}: {Status}, start {CanStart}", mode, tool.Name, decision.Status, decision.CanStart);
                return decision;
            }
            catch (Exception ex)
            {
                // Never lock the user out because of a bug in the guard: fail open in monitor, closed in block.
                _logger.LogError(ex, "Egress Guard check failed for {Tool}", tool.Name);
                return new GateDecision(mode != EgressMode.Block, GuardStatus.Unreviewed, "The Egress Guard could not check this tool.");
            }
        });
    }
}
