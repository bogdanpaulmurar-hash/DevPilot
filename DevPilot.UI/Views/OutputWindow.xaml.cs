using System.Windows;

namespace DevPilot.UI.Views;

/// <summary>Shows the full text of a git pull, an update or a command, like a console would.</summary>
public partial class OutputWindow : Window
{
    private OutputWindow(string title, string text)
    {
        InitializeComponent();
        Title = title;
        Body.Text = text;
        Body.ScrollToEnd();
    }

    public static void ShowText(string title, string text)
    {
        var w = new OutputWindow(title, text);
        if (Application.Current.MainWindow is { IsVisible: true } main && !ReferenceEquals(main, w)) w.Owner = main;
        w.Show();
        w.Activate();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Body.Text); }
        catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy: nothing to do */ }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
