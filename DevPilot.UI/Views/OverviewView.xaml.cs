using System.Windows;
using System.Windows.Controls;
using DevPilot.UI.Hub;
using DevPilot.UI.ViewModels;

namespace DevPilot.UI.Views;

public partial class OverviewView : UserControl
{
    private readonly FlyoutController _flyouts = new() { Side = Models.FlyoutSide.Right, HoverDelayMs = 200 };

    public OverviewView()
    {
        InitializeComponent();
        Unloaded += (_, _) => _flyouts.CloseAll();
        PreviewMouseDown += (_, e) =>
        {
            if (_flyouts.IsOpen && !_flyouts.IsMouseOverAny) _flyouts.CloseAll();
        };
        _flyouts.Invoked += _ => _flyouts.CloseAll();
    }

    private OverviewViewModel? Vm => DataContext as OverviewViewModel;

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not HubToolVM tool || Vm is null) return;
        Open(fe, tool);
    }

    private void SpotlightMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && Vm?.Hub.Spotlight is { } sp) Open(fe, sp.Tool);
    }

    private void Open(FrameworkElement anchor, HubToolVM tool)
    {
        var look = App.GetService<DevPilot.Core.Interfaces.IConfigStore>().Current.Hub;
        _flyouts.Width = look.FlyoutWidth;
        _flyouts.FontSize = look.FontSize;
        _flyouts.OpenNow(anchor, Vm!.Hub.Flyouts.ForTool(tool.Model), tool.Name, tool.StatusText);
    }
}
