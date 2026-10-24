using System.Windows;
using System.Windows.Controls;

namespace DevPilot.UI.Hub;

/// <summary>Picks the tool row or the separator row template for an item in a hub group.</summary>
public sealed class HubRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Row { get; set; }
    public DataTemplate? Separator { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is HubToolVM { IsSeparator: true } ? Separator : Row;
}
