using System.ComponentModel;
using System.Windows;
using DevPilot.Core.Interfaces;
using DevPilot.UI.ViewModels;

namespace DevPilot.UI.Views;

public partial class MainWindow : Window
{
    private readonly IConfigStore _config;
    public bool AllowClose { get; set; }
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel vm, IConfigStore config)
    {
        InitializeComponent();
        ViewModel = vm;
        _config = config;
        DataContext = vm;
        var w = config.Current.Window;
        Width = Math.Max(MinWidth, w.Width);
        Height = Math.Max(MinHeight, w.Height);
        StateChanged += (_, _) =>
        {
            MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
            // WindowChrome maximised windows overhang the screen by the resize border.
            RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        };
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            _config.Current.Window.Width = ActualWidth;
            _config.Current.Window.Height = ActualHeight;
            _config.Touch(ConfigArea.Window);
        }
        if (!AllowClose && _config.Current.Window.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
        if (!AllowClose) Dispatcher.BeginInvoke(() => App.GetService<Services.IAppShell>().Exit());
    }
}
