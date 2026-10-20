namespace DevPilot.Models;

/// <summary>
/// One option of a tool (what used to live in the tool's own tray menu).
/// Shown in the hub flyout when hovering the tool, and in the tool's DevPilot tray icon.
/// </summary>
public class ToolAction
{
    public string Id { get; set; } = NewId.Short();
    public string Label { get; set; } = string.Empty;
    /// <summary>Optional monochrome symbol (Segoe Fluent / MDL2 glyph or any character).</summary>
    public string Glyph { get; set; } = string.Empty;
    public ActionKind Kind { get; set; } = ActionKind.Arguments;
    /// <summary>Program / URL / folder / file, depending on <see cref="Kind"/>. Environment variables and {tokens} are expanded.</summary>
    public string Value { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    /// <summary>Optional grouping label inside the flyout (actions with the same section are shown together).</summary>
    public string Section { get; set; } = string.Empty;
    /// <summary>Shown as a large button on the spotlight card (ScreenPilot).</summary>
    public bool Featured { get; set; }
    public bool Bold { get; set; }

    public ToolAction Clone() => (ToolAction)MemberwiseClone();
}
