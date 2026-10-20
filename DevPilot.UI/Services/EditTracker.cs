using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using DevPilot.Core.Interfaces;

namespace DevPilot.UI.Services;

/// <summary>
/// Attached behaviour: any edit (text, toggle, selection, slider) inside an element marks the given
/// configuration area as changed. Bindings write straight into the settings objects; this makes the
/// change visible to the rest of the app (hub rebuild, save) with a short debounce.
/// </summary>
public static class EditTracker
{
    public static readonly DependencyProperty AreaProperty = DependencyProperty.RegisterAttached(
        "Area", typeof(object), typeof(EditTracker), new PropertyMetadata(null, OnAreaChanged));

    public static ConfigArea? GetArea(DependencyObject d) => d.GetValue(AreaProperty) is ConfigArea a ? a : null;
    public static void SetArea(DependencyObject d, object? value) => d.SetValue(AreaProperty, value);

    /// <summary>Raised after the area was touched (pages use it to refresh derived text).</summary>
    public static event Action<ConfigArea>? Edited;

    private static readonly HashSet<ConfigArea> Pending = [];
    private static DispatcherTimer? _timer;
    private static int _suppress;

    public static IDisposable Suppress()
    {
        _suppress++;
        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            // Release after bindings have settled.
            Application.Current.Dispatcher.BeginInvoke(() => _suppress--, DispatcherPriority.ContextIdle);
        }
    }

    private static void OnAreaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el || e.OldValue is not null) return;
        el.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, a) => Handle(a)));
        el.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((s, a) => Handle(a)));
        el.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((s, a) => Handle(a)));
        el.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((s, a) => Handle(a)));
        el.AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((s, a) => Handle(a)));
    }

    private static void Handle(RoutedEventArgs e)
    {
        if (_suppress > 0 || e.OriginalSource is not FrameworkElement source || !source.IsLoaded) return;
        if (source is ListBox) return; // list selection is navigation, not an edit
        if (GetNoTrack(source)) return;
        ConfigArea? area = null;
        DependencyObject? cur = source;
        while (cur is not null && area is null)
        {
            area = GetArea(cur);
            cur = System.Windows.Media.VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur);
        }
        if (area is null) return;
        Pending.Add(area.Value);
        _timer ??= CreateTimer();
        _timer.Stop();
        _timer.Start();
    }

    public static readonly DependencyProperty NoTrackProperty = DependencyProperty.RegisterAttached(
        "NoTrack", typeof(bool), typeof(EditTracker), new PropertyMetadata(false));
    public static bool GetNoTrack(DependencyObject d) => (bool)d.GetValue(NoTrackProperty);
    public static void SetNoTrack(DependencyObject d, bool value) => d.SetValue(NoTrackProperty, value);

    private static DispatcherTimer CreateTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            var config = App.GetService<IConfigStore>();
            foreach (var area in Pending.ToList())
            {
                config.Touch(area);
                Edited?.Invoke(area);
            }
            Pending.Clear();
        };
        return t;
    }
}
