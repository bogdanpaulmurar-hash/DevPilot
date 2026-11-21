using DevPilot.Models;

namespace DevPilot.Core.Hub;

/// <summary>Tools shown as buttons in the hub's top bar, next to the Repositories button.</summary>
public static class HubHeader
{
    /// <summary>The bar holds DevPilot's own buttons too; more than two tools would push the title out of a narrow hub.</summary>
    public const int MaxTools = 2;

    /// <summary>Pinned, visible tools in hub order (group order, then row order), at most <see cref="MaxTools"/>.</summary>
    public static IReadOnlyList<ToolEntry> Pinned(AppSettings s)
    {
        var groupOrder = s.Groups.Where(g => !g.Hidden).ToDictionary(g => g.Id, g => g.Order);
        return s.Tools
            .Where(t => t.PinToHubHeader && !t.Hidden && !t.IsSeparator && groupOrder.ContainsKey(t.GroupId))
            .OrderBy(t => groupOrder[t.GroupId]).ThenBy(t => t.Order)
            .Take(MaxTools)
            .ToList();
    }
}
