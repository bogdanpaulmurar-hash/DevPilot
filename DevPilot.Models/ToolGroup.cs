namespace DevPilot.Models;

/// <summary>User defined group shown as a titled section in the hub.</summary>
public class ToolGroup
{
    public string Id { get; set; } = NewId.Short();
    public string Title { get; set; } = "Group";
    public string Glyph { get; set; } = string.Empty;
    /// <summary>Optional marker colour (#RRGGBB).</summary>
    public string Color { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool Collapsed { get; set; }
    public bool Hidden { get; set; }
}
