using System.Windows;
using System.Windows.Controls;
using DevPilot.Core.Presets;
using DevPilot.UI.ViewModels;

namespace DevPilot.UI.Views;

public partial class ToolsView : UserControl
{
    public ToolsView() => InitializeComponent();

    private ToolsViewModel? Vm => DataContext as ToolsViewModel;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Button button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(Item("Program or script", Glyphs.App, () => Vm.AddToolCommand.Execute(null)));
        menu.Items.Add(Item("Folder…", Glyphs.Folder, () => Vm.AddFolderCommand.Execute(null)));
        menu.Items.Add(Item("Web link", Glyphs.Globe, () => Vm.AddLinkCommand.Execute(null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Your known tools", IsEnabled = false });
        foreach (var preset in ToolPresets.All)
        {
            var p = preset;
            menu.Items.Add(Item(p.Name, p.Glyph, () => Vm.AddPresetCommand.Execute(p), p.Description));
        }
        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, string glyph, Action onClick, string? tip = null)
    {
        var item = new MenuItem
        {
            Header = header,
            Icon = new TextBlock { Text = glyph, Style = (Style)Application.Current.FindResource("Glyph") },
            ToolTip = tip,
        };
        item.Click += (_, _) => onClick();
        return item;
    }
}
