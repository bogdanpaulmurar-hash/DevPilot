using DevPilot.Models;

namespace DevPilot.Core.Hub;

/// <summary>Which parts of a hub row's label are drawn.</summary>
public readonly record struct LabelParts(bool Logo, bool Symbol, bool Name)
{
    public static readonly LabelParts Classic = new(false, true, true);
}

/// <summary>
/// Resolves a row's <see cref="HubLabelMode"/> (its own, or the hub-wide one) into what is actually drawn.
/// A mode that needs a logo falls back to the classic symbol + name when the entry has none, so a row is never empty.
/// </summary>
public static class HubLabel
{
    /// <summary>The hub-wide mode can't be <see cref="HubLabelMode.Default"/> (or an unknown value): those become the classic look.</summary>
    public static HubLabelMode HubDefault(HubLabelMode mode) =>
        mode is HubLabelMode.Default || !Enum.IsDefined(mode) ? HubLabelMode.TextAndSymbol : mode;

    /// <summary>The entry's own mode, or the hub-wide one when the entry follows the hub.</summary>
    public static HubLabelMode Effective(HubLabelMode own, HubLabelMode hub) =>
        own is HubLabelMode.Default || !Enum.IsDefined(own) ? HubDefault(hub) : own;

    /// <summary>True when the mode can show a logo, i.e. it is worth loading one.</summary>
    public static bool WantsLogo(HubLabelMode effective) =>
        effective is HubLabelMode.Auto or HubLabelMode.LogoAndText or HubLabelMode.LogoOnly;

    public static LabelParts Resolve(HubLabelMode own, HubLabelMode hub, bool hasLogo) =>
        Resolve(Effective(own, hub), hasLogo);

    public static LabelParts Resolve(HubLabelMode effective, bool hasLogo) => effective switch
    {
        HubLabelMode.TextOnly => new(false, false, true),
        HubLabelMode.LogoOnly when hasLogo => new(true, false, false),
        HubLabelMode.Auto or HubLabelMode.LogoAndText when hasLogo => new(true, false, true),
        _ => LabelParts.Classic,
    };
}
