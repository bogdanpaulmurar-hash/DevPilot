using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Typer;
using DevPilot.Models;

namespace DevPilot.UI.Typer;

/// <summary>
/// Floating HUD for the Virtual Typer. It never takes the keyboard focus (WS_EX_NOACTIVATE), otherwise the keystrokes
/// would land in the HUD instead of the target window. Esc is registered as a global hotkey only while a run is active.
/// </summary>
public partial class TyperHudWindow : Window
{
    private const int WM_HOTKEY = 0x0312, WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
    private const int EscHotkeyId = 0x7E57;
    private const uint VK_ESCAPE = 0x1B, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    private readonly IConfigStore _config;
    private readonly IKeyboardSender _sender;
    private readonly IForegroundProbe _probe;
    private readonly ClipboardHistory _history;
    private readonly ClipboardWatcher _watcher;
    private HwndSource? _source;
    private List<ClipboardEntry> _entries = [];
    private ClipboardEntry? _selected;
    private TypingPlan _plan = TypingPlan.Create(null);
    private TyperSession? _session;
    private bool _confirmed;
    private bool _loading = true;

    public TyperHudWindow(IConfigStore config, IKeyboardSender sender, IForegroundProbe probe,
        ClipboardHistory history, ClipboardWatcher watcher)
    {
        InitializeComponent();
        _config = config;
        _sender = sender;
        _probe = probe;
        _history = history;
        _watcher = watcher;

        var s = config.Current.Typer;
        DelaySlider.Value = Math.Clamp(s.DelayMs, 0, (int)DelaySlider.Maximum);
        CountdownSlider.Value = Math.Clamp(s.CountdownSeconds, 0, (int)CountdownSlider.Maximum);
        ShiftEnterBox.IsChecked = s.LineBreakAsShiftEnter;
        HistoryBox.IsChecked = s.KeepClipboardHistory;
        UpdateLabels();
        _loading = false;
        PlaceWindow();
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => { _session?.Stop(); ReleaseEscape(); };
    }

    /// <summary>True while a countdown or typing run is active.</summary>
    public bool IsRunning => _session is { State: TyperState.Countdown or TyperState.Typing };

    /// <summary>Re-reads the clipboard and shows the window without activating it.</summary>
    public void Present()
    {
        if (IsRunning) return;
        LoadClipboard();
        if (!IsVisible) Show();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_NOACTIVATE));
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        if (msg == WM_HOTKEY && wParam.ToInt32() == EscHotkeyId)
        {
            handled = true;
            _session?.Stop();
        }
        return IntPtr.Zero;
    }

    private void PlaceWindow()
    {
        var s = _config.Current.Typer;
        var area = SystemParameters.WorkArea;
        var left = s.HudLeft ?? area.Right - Width - 24;
        var top = s.HudTop ?? area.Bottom - 360;
        // Keep it on a screen even if the monitor layout changed since the last run.
        Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60));
        Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60));
    }

    private void LoadClipboard()
    {
        string? text = null;
        try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); }
        catch (COMException) { /* clipboard locked by another app; shown as empty, user can retry with ↻ */ }

        // The collected history plus the current clipboard, which may be missing from it (history off, excluded text).
        _entries = _history.Items.ToList();
        var current = _entries.FirstOrDefault(e => e.Text == text);
        if (current is null && !string.IsNullOrWhiteSpace(text))
        {
            current = new ClipboardEntry(text, DateTime.Now, false);
            _entries.Insert(0, current);
        }
        Select(current ?? _entries.FirstOrDefault());
    }

    private void Select(ClipboardEntry? entry)
    {
        _selected = entry;
        _confirmed = false;
        _plan = TypingPlan.Create(entry?.Text, TypingOptions.From(_config.Current.Typer).MaxChars);
        PreviewText.Text = _plan.IsEmpty ? "(no text in the clipboard)" : _plan.Preview();
        InfoText.Text = _plan.IsEmpty ? string.Empty
            : $"{_plan.Count:N0} keystrokes" + (_plan.Truncated ? " (cut at the safety limit)" : string.Empty);
        StatusText.Text = string.Empty;
        Bar.Value = 0;
        StartButton.IsEnabled = !_plan.IsEmpty;
        StartButton.Content = "Start typing";
        BuildHistoryList();
    }

    private void BuildHistoryList()
    {
        HistorySection.Visibility = _entries.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Children.Clear();
        foreach (var entry in _entries)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = entry.Summary(), TextTrimming = TextTrimming.CharacterEllipsis });
            var length = new TextBlock { Text = entry.Length.ToString("N0"), Margin = new Thickness(10, 0, 0, 0), Opacity = 0.6 };
            Grid.SetColumn(length, 1);
            row.Children.Add(length);

            var selected = ReferenceEquals(entry, _selected);
            var button = new Button
            {
                Content = row,
                Tag = entry,
                Height = 30,
                MinHeight = 0,
                Padding = new Thickness(10, 0, 10, 0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Style = (Style)FindResource("GhostButton"),
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = (Brush)FindResource(selected ? "TextBrush" : "TextSecondaryBrush"),
            };
            if (selected) button.Background = (Brush)FindResource("HoverBrush");
            button.Click += OnPickEntry;
            HistoryPanel.Children.Add(button);
        }
    }

    private void OnPickEntry(object sender, RoutedEventArgs e)
    {
        if (!IsRunning && sender is Button { Tag: ClipboardEntry entry }) Select(entry);
    }

    private void OnClearHistory(object sender, RoutedEventArgs e)
    {
        if (IsRunning) return;
        _history.Clear();
        LoadClipboard();
    }

    private void OnHistoryToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var on = HistoryBox.IsChecked == true;
        _config.Current.Typer.KeepClipboardHistory = on;
        _config.Touch(ConfigArea.Window);
        _watcher.SetEnabled(on);
        if (!IsRunning) LoadClipboard();
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (IsRunning || _plan.IsEmpty) return;
        var options = TypingOptions.From(_config.Current.Typer);
        if (options.ConfirmAboveChars > 0 && _plan.Count > options.ConfirmAboveChars && !_confirmed)
        {
            _confirmed = true;
            StartButton.Content = $"Confirm: type {_plan.Count:N0} keys";
            return;
        }

        var session = new TyperSession(_sender, _probe);
        _session = session;
        session.Progress += p => Dispatcher.BeginInvoke(() => ShowProgress(p));
        SetRunning(true);
        if (!TryCaptureEscape()) StatusText.Text = "Esc is used by another app. Use the Stop button.";
        try { await session.RunAsync(_plan, options); }
        finally
        {
            ReleaseEscape();
            SetRunning(false);
        }
    }

    private void OnStop(object sender, RoutedEventArgs e) => _session?.Stop();

    private void OnReload(object sender, RoutedEventArgs e)
    {
        if (!IsRunning) LoadClipboard();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        _session?.Stop();
        SavePosition();
        Hide();
    }

    private void OnTitleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { Name: "CloseButton" }) return;
        DragMove();
        SavePosition();
    }

    private void OnSettingChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var s = _config.Current.Typer;
        s.DelayMs = (int)DelaySlider.Value;
        s.CountdownSeconds = (int)CountdownSlider.Value;
        UpdateLabels();
        _config.Touch(ConfigArea.Window);
    }

    private void OnLineBreakChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.Current.Typer.LineBreakAsShiftEnter = ShiftEnterBox.IsChecked == true;
        _config.Touch(ConfigArea.Window);
    }

    private void UpdateLabels()
    {
        DelayLabel.Text = $"{(int)DelaySlider.Value} ms";
        CountdownLabel.Text = $"{(int)CountdownSlider.Value} s";
    }

    private void SavePosition()
    {
        var s = _config.Current.Typer;
        s.HudLeft = Left;
        s.HudTop = Top;
        _config.Touch(ConfigArea.Window);
    }

    private void SetRunning(bool running)
    {
        StartButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ReloadButton.IsEnabled = !running;
        DelaySlider.IsEnabled = CountdownSlider.IsEnabled = ShiftEnterBox.IsEnabled = HistoryBox.IsEnabled = HistorySection.IsEnabled = !running;
        if (!running)
        {
            _confirmed = false;
            StartButton.Content = "Start typing";
        }
    }

    private bool TryCaptureEscape() =>
        _source is not null && RegisterHotKey(_source.Handle, EscHotkeyId, MOD_NOREPEAT, VK_ESCAPE);

    private void ReleaseEscape()
    {
        if (_source is not null) UnregisterHotKey(_source.Handle, EscHotkeyId);
    }

    private void ShowProgress(TyperProgress p)
    {
        Bar.Value = p.Total == 0 ? 0 : (double)p.Sent / p.Total;
        StatusText.Text = p.State switch
        {
            TyperState.Countdown => $"Click in the target field. Typing starts in {p.SecondsLeft} s…",
            TyperState.Typing => $"Typing… {p.Sent:N0} / {p.Total:N0}",
            TyperState.Done => $"Done: {p.Sent:N0} keystrokes sent.",
            TyperState.Stopped when p.Reason == TyperStopReason.WindowChanged => $"Stopped: the active window changed ({p.Sent:N0} / {p.Total:N0} sent).",
            TyperState.Stopped => $"Stopped ({p.Sent:N0} / {p.Total:N0} sent).",
            TyperState.Failed => $"Windows rejected the input after {p.Sent:N0} keystrokes (an elevated window in front?).",
            _ => StatusText.Text,
        };
    }
}
