using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DevPilot.Core.Typer;

namespace DevPilot.UI.Typer;

/// <summary>
/// Feeds <see cref="ClipboardHistory"/> from clipboard change notifications (an OS event, no polling).
/// Texts that password managers mark as excluded from clipboard history are never stored.
/// </summary>
public sealed class ClipboardWatcher : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private readonly ClipboardHistory _history;
    private HwndSource? _source;
    private bool _reading;

    public ClipboardWatcher(ClipboardHistory history) => _history = history;

    public bool IsEnabled => _source is not null;

    /// <summary>Turning it off also forgets what was collected.</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled == IsEnabled) return;
        if (enabled)
        {
            var p = new HwndSourceParameters("DevPilotClipboard") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
            if (!AddClipboardFormatListener(_source.Handle))
            {
                _source.Dispose();
                _source = null;
                return;
            }
            RequestRead();
        }
        else
        {
            Stop();
            _history.Clear();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            handled = true;
            RequestRead();
        }
        return IntPtr.Zero;
    }

    private void RequestRead()
    {
        if (_reading) return;
        _reading = true;
        Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            try { await ReadAsync(); }
            finally { _reading = false; }
        });
    }

    private async Task ReadAsync()
    {
        // The app that just copied may still hold the clipboard open; try again shortly.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Clipboard.ContainsText() && !IsExcluded()) _history.Add(Clipboard.GetText());
                return;
            }
            catch (COMException)
            {
                await Task.Delay(40 * (attempt + 1));
            }
        }
    }

    private static bool IsExcluded()
    {
        if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return true;
        if (Clipboard.ContainsData("CanIncludeInClipboardHistory")
            && Clipboard.GetData("CanIncludeInClipboardHistory") is MemoryStream ms)
        {
            var bytes = ms.ToArray();
            if (bytes.Length > 0 && bytes[0] == 0) return true;
        }
        return false;
    }

    private void Stop()
    {
        if (_source is null) return;
        RemoveClipboardFormatListener(_source.Handle);
        _source.Dispose();
        _source = null;
    }

    public void Dispose() => Stop();
}
