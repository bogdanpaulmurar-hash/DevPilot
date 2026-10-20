using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DevPilot.UI.Hub;

public partial class FlyoutPanel : UserControl
{
    public int Level { get; }
    public event Action<FlyoutPanel, FlyoutItem, FrameworkElement>? ItemHovered;
    public event Action<FlyoutPanel, FlyoutItem, FrameworkElement>? ItemInvoked;

    public FlyoutPanel(int level, IReadOnlyList<FlyoutItem> items, double width, double fontSize, string? title = null, string? subtitle = null)
    {
        InitializeComponent();
        Level = level;
        FontSize = fontSize;
        Frame.Width = width;
        Items.ItemsSource = items;
        if (!string.IsNullOrWhiteSpace(title))
        {
            TitleBar.Visibility = Visibility.Visible;
            TitleText.Text = title;
            SubtitleText.Text = subtitle ?? string.Empty;
            SubtitleText.Visibility = string.IsNullOrWhiteSpace(subtitle) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void Row_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FlyoutItem item)
            ItemHovered?.Invoke(this, item, fe);
    }

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FlyoutItem item)
        {
            e.Handled = true;
            ItemInvoked?.Invoke(this, item, fe);
        }
    }
}
