using DevPilot.Models;

namespace DevPilot.Core.Config;

/// <summary>Ordering helpers for groups and tools.</summary>
public static class Ordering
{
    public static void Renumber(AppSettings s)
    {
        int i = 0;
        foreach (var g in s.Groups.OrderBy(g => g.Order).ToList()) g.Order = i++;
        s.Groups.Sort((a, b) => a.Order.CompareTo(b.Order));
        foreach (var grp in s.Tools.GroupBy(t => t.GroupId))
        {
            int j = 0;
            foreach (var t in grp.OrderBy(t => t.Order)) t.Order = j++;
        }
    }

    public static IEnumerable<ToolEntry> ToolsOf(AppSettings s, string groupId) =>
        s.Tools.Where(t => t.GroupId == groupId).OrderBy(t => t.Order);

    /// <summary>Moves a tool up (-1) or down (+1) inside its group.</summary>
    public static bool MoveTool(AppSettings s, ToolEntry tool, int delta)
    {
        var list = ToolsOf(s, tool.GroupId).ToList();
        int idx = list.IndexOf(tool);
        int target = idx + delta;
        if (idx < 0 || target < 0 || target >= list.Count) return false;
        (list[idx].Order, list[target].Order) = (list[target].Order, list[idx].Order);
        Renumber(s);
        return true;
    }

    /// <summary>Moves a tool into another group, at the end (or at <paramref name="index"/>).</summary>
    public static void MoveToGroup(AppSettings s, ToolEntry tool, string groupId, int index = int.MaxValue)
    {
        var list = ToolsOf(s, groupId).Where(t => t != tool).ToList();
        index = Math.Clamp(index, 0, list.Count);
        list.Insert(index, tool);
        tool.GroupId = groupId;
        for (int i = 0; i < list.Count; i++) list[i].Order = i;
        Renumber(s);
    }

    public static bool MoveGroup(AppSettings s, ToolGroup group, int delta)
    {
        var list = s.Groups.OrderBy(g => g.Order).ToList();
        int idx = list.IndexOf(group);
        int target = idx + delta;
        if (idx < 0 || target < 0 || target >= list.Count) return false;
        (list[idx].Order, list[target].Order) = (list[target].Order, list[idx].Order);
        Renumber(s);
        return true;
    }

    public static bool MoveAction(ToolEntry tool, ToolAction action, int delta)
    {
        int idx = tool.Actions.IndexOf(action);
        int target = idx + delta;
        if (idx < 0 || target < 0 || target >= tool.Actions.Count) return false;
        (tool.Actions[idx], tool.Actions[target]) = (tool.Actions[target], tool.Actions[idx]);
        return true;
    }
}
