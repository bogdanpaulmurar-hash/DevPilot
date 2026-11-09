namespace DevPilot.Core.Activity;

/// <summary>One running action. Dispose it when the action ends.</summary>
public interface IActivityScope : IDisposable
{
    /// <summary>Sets progress as a fraction 0..1, or null for an indeterminate bar.</summary>
    void Report(double? fraction, string? detail = null);
}

/// <summary>Snapshot of what the progress bar should show.</summary>
public sealed record ActivityState(bool IsActive, string Label, double? Fraction)
{
    public static readonly ActivityState Idle = new(false, string.Empty, null);
}

/// <summary>
/// Keeps track of running actions (backup, launching a tool, repository pulls) so the UI can show one
/// small progress bar. Thread-safe; raises <see cref="Changed"/> on any thread.
/// </summary>
public sealed class ActivityTracker
{
    private readonly object _lock = new();
    private readonly List<Scope> _scopes = [];

    public event Action? Changed;

    public ActivityState Current
    {
        get
        {
            lock (_lock)
            {
                if (_scopes.Count == 0) return ActivityState.Idle;
                // Show the most recent action; mention how many others run alongside it.
                var top = _scopes[^1];
                var label = top.Label + (top.Detail.Length > 0 ? " · " + top.Detail : string.Empty);
                if (_scopes.Count > 1) label += $" (+{_scopes.Count - 1})";
                return new ActivityState(true, label, _scopes.Count == 1 ? top.Fraction : null);
            }
        }
    }

    public IActivityScope Begin(string label)
    {
        var scope = new Scope(this, label);
        lock (_lock) _scopes.Add(scope);
        Changed?.Invoke();
        return scope;
    }

    private void End(Scope scope)
    {
        bool removed;
        lock (_lock) removed = _scopes.Remove(scope);
        if (removed) Changed?.Invoke();
    }

    private sealed class Scope(ActivityTracker owner, string label) : IActivityScope
    {
        public string Label { get; } = label;
        public string Detail { get; private set; } = string.Empty;
        public double? Fraction { get; private set; }

        public void Report(double? fraction, string? detail = null)
        {
            lock (owner._lock)
            {
                Fraction = fraction is null ? null : Math.Clamp(fraction.Value, 0, 1);
                Detail = detail ?? Detail;
            }
            owner.Changed?.Invoke();
        }

        public void Dispose() => owner.End(this);
    }
}
