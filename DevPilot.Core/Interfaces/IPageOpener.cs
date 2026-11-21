using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

/// <summary>Opens one of DevPilot's own pages for a tool (actions of kind <see cref="ActionKind.Page"/>).</summary>
public interface IPageOpener
{
    /// <summary>False when DevPilot has no page with that key.</summary>
    bool Open(string page, ToolEntry tool);
}
