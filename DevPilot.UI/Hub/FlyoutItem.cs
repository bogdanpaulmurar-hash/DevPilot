using System.Windows.Input;
using DevPilot.UI.Mvvm;

namespace DevPilot.UI.Hub;

public enum FlyoutItemKind { Action, Header, Separator, Info }

/// <summary>One row of a flyout (tool options, folder content, quick menu).</summary>
public sealed class FlyoutItem : ObservableObject
{
    public FlyoutItemKind Kind { get; init; } = FlyoutItemKind.Action;
    public string Label { get; init; } = string.Empty;
    public string Glyph { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string ToolTip { get; init; } = string.Empty;
    public bool IsBold { get; init; }
    public bool IsAccent { get; init; }
    public bool IsDanger { get; init; }
    public bool IsEnabled { get; init; } = true;
    /// <summary>Runs when the row is clicked. Returning false keeps the hub open.</summary>
    public Func<Task<bool>>? Invoke { get; init; }
    /// <summary>Lazily builds a nested flyout (sub-folders, sub-menus).</summary>
    public Func<IReadOnlyList<FlyoutItem>>? Children { get; init; }

    public bool HasChildren => Children is not null;
    public bool IsAction => Kind == FlyoutItemKind.Action;
    public bool IsHeader => Kind == FlyoutItemKind.Header;
    public bool IsSeparator => Kind == FlyoutItemKind.Separator;
    public bool IsInfo => Kind == FlyoutItemKind.Info;
    public bool HasGlyph => !string.IsNullOrEmpty(Glyph);

    public static FlyoutItem Separator() => new() { Kind = FlyoutItemKind.Separator };
    public static FlyoutItem Header(string text) => new() { Kind = FlyoutItemKind.Header, Label = text };
    public static FlyoutItem Info(string text, string glyph = "") => new() { Kind = FlyoutItemKind.Info, Label = text, Glyph = glyph };

    public static FlyoutItem Action(string label, string glyph, Func<Task<bool>> invoke, string detail = "", bool bold = false) =>
        new() { Label = label, Glyph = glyph, Invoke = invoke, Detail = detail, IsBold = bold };

    public static FlyoutItem Action(string label, string glyph, Action invoke, string detail = "", bool bold = false) =>
        new() { Label = label, Glyph = glyph, Invoke = () => { invoke(); return Task.FromResult(true); }, Detail = detail, IsBold = bold };
}
