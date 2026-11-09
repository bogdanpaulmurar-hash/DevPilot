using System.Windows;
using DevPilot.Core.Activity;
using DevPilot.UI.Mvvm;

namespace DevPilot.UI.ViewModels;

/// <summary>What the small progress bar shows. Updates are marshalled to the UI thread.</summary>
public sealed class ActivityViewModel : ObservableObject
{
    private readonly ActivityTracker _tracker;
    private ActivityState _state = ActivityState.Idle;

    public ActivityViewModel(ActivityTracker tracker)
    {
        _tracker = tracker;
        _tracker.Changed += OnChanged;
    }

    public bool IsActive => _state.IsActive;
    public string Label => _state.Label;
    public bool IsIndeterminate => _state.Fraction is null;
    public double Value => _state.Fraction ?? 0;

    private void OnChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Refresh();
        else dispatcher.BeginInvoke(Refresh);
    }

    private void Refresh()
    {
        _state = _tracker.Current;
        RefreshAll();
    }
}
