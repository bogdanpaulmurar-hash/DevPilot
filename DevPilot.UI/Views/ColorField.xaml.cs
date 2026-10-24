using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DevPilot.UI.Views;

/// <summary>Colour picker used in the tool, group and separator editors: palette, hex box and "None".</summary>
public partial class ColorField : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(ColorField),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault)
        {
            DefaultUpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });

    public ColorField() => InitializeComponent();

    /// <summary>"#RRGGBB" or empty (theme colour).</summary>
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex }) Value = hex;
    }

    private void None_Click(object sender, RoutedEventArgs e) => Value = string.Empty;
}
