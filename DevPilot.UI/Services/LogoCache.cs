using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DevPilot.Core.Hub;
using DevPilot.Core.Presets;
using DevPilot.Models;

namespace DevPilot.UI.Services;

/// <summary>
/// Hub logos. Each source file is read once, normalised to a 64 px square PNG (sharp up to 200% display scaling)
/// and kept in %LOCALAPPDATA%\DevPilot\logo-cache plus in memory. Reading never blocks the UI: file checks run on the
/// thread pool with a timeout (offline drives, unsynced cloud files) and decoding runs on one low-priority STA thread.
/// A logo that can't be read just isn't shown, so the row keeps its symbol.
/// </summary>
public sealed class LogoCache
{
    public static LogoCache Shared { get; } = new();

    /// <summary>How long a row waits for its logo before giving up (the load still finishes and is cached).</summary>
    public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    private const int MaxCacheFiles = 300;

    private readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevPilot", "logo-cache");
    private readonly ConcurrentDictionary<string, LogoRef> _refs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, BitmapSource?> _images = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<BitmapSource?>> _pending = new(StringComparer.Ordinal);
    private readonly StaWorker _worker = new("DevPilot logos");
    private int _pruned;

    public string Folder => _dir;

    /// <summary>Instant answer from memory, for rows that are rebuilt often (hub refresh, editor preview).</summary>
    public bool TryGetCached(ToolEntry tool, out BitmapSource? logo)
    {
        logo = null;
        if (!_refs.TryGetValue(RefKey(tool), out var r)) return false;
        if (r.IsNone) return true;
        return _images.TryGetValue(ImageKey(r.Path), out logo);
    }

    /// <summary>The tool's logo, or null when it has none or it can't be read in time.</summary>
    public async Task<BitmapSource?> GetAsync(ToolEntry tool)
    {
        if (tool.IsSeparator) return null;
        try
        {
            var refKey = RefKey(tool);
            if (!_refs.TryGetValue(refKey, out var logoRef))
            {
                // The manifest is only looked up when the user did not pick a file (it means reading the tool's folder).
                var snapshot = new ToolEntry { IconPath = tool.IconPath, Path = tool.Path, Kind = tool.Kind, WorkingDirectory = tool.WorkingDirectory, Name = tool.Name };
                var resolve = Task.Run(() => LogoSource.Resolve(snapshot,
                    string.IsNullOrWhiteSpace(snapshot.IconPath) ? ToolManifest.IconFor(snapshot) : null));
                if (await Task.WhenAny(resolve, Task.Delay(Timeout)) != resolve) return null;
                logoRef = await resolve;
                _refs[refKey] = logoRef;
            }
            if (logoRef.IsNone) return null;

            var imageKey = ImageKey(logoRef.Path);
            if (_images.TryGetValue(imageKey, out var known)) return known;

            var load = _pending.GetOrAdd(imageKey, _ => LoadAsync(logoRef.Path, imageKey));
            return await Task.WhenAny(load, Task.Delay(Timeout)) == load ? await load : null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"DevPilot logo for '{tool.Name}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Forgets what was resolved for this tool (after its logo, path or kind changed in the editor).</summary>
    public void Forget(ToolEntry tool)
    {
        if (_refs.TryRemove(RefKey(tool), out var r) && !r.IsNone) _images.TryRemove(ImageKey(r.Path), out _);
    }

    private async Task<BitmapSource?> LoadAsync(string path, string imageKey)
    {
        try
        {
            // File checks on the thread pool: a slow network path must not hold up the decoder thread.
            var info = await Task.Run(() =>
            {
                var fi = new FileInfo(path);
                return fi.Exists ? fi : null;
            });
            // A missing file is not remembered: it may appear later (a cloud drive that finishes syncing).
            if (info is null) return null;
            var cacheFile = Path.Combine(_dir, LogoSource.CacheKey(info.FullName, info.Length, info.LastWriteTimeUtc));
            var image = await _worker.Run(() => File.Exists(cacheFile) ? ReadPng(cacheFile) : Build(info.FullName, cacheFile));
            _images[imageKey] = image;
            if (Interlocked.Exchange(ref _pruned, 1) == 0) _ = Task.Run(Prune);
            return image;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"DevPilot logo '{path}': {ex.Message}");
            _images[imageKey] = null;
            return null;
        }
        finally
        {
            _pending.TryRemove(imageKey, out _);
        }
    }

    // ── Runs on the STA worker ──────────────────────────────────────────

    private static BitmapSource ReadPng(string file)
    {
        using var stream = File.OpenRead(file);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        return frame;
    }

    private BitmapSource? Build(string source, string cacheFile)
    {
        var raw = Extract(source);
        if (raw is null) return null;
        var square = Square(raw);
        try
        {
            Directory.CreateDirectory(_dir);
            var tmp = cacheFile + ".tmp";
            using (var fs = File.Create(tmp))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(square));
                encoder.Save(fs);
            }
            File.Move(tmp, cacheFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"DevPilot logo cache write: {ex.Message}"); // still shown, just not cached on disk
        }
        return square;
    }

    private static BitmapSource? Extract(string path)
    {
        if (LogoSource.IsImage(path) || LogoSource.IsIcoFile(path))
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            // .ico files hold several sizes: take the largest so the logo is sharp, not an upscaled 16 px frame.
            return decoder.Frames.OrderByDescending(f => f.PixelWidth).ThenByDescending(f => f.Format.BitsPerPixel).First();
        }
        // Programs, shortcuts and documents: ask Windows for a large icon (up to 256 px) instead of the 32 px
        // ExtractAssociatedIcon gives, which looked pixelated when scaled.
        return ShellIcon.Get(path, LogoSource.CachePixels) ?? ShellIcon.Associated(path);
    }

    /// <summary>Fits the image into a transparent 64 px square with high-quality scaling.</summary>
    private static BitmapSource Square(BitmapSource src)
    {
        int size = LogoSource.CachePixels;
        var (w, h) = LogoSource.FitInto(src.PixelWidth, src.PixelHeight, size);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
            dc.DrawImage(src, new Rect((size - w) / 2.0, (size - h) / 2.0, w, h));
        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    // ── Housekeeping ────────────────────────────────────────────────────

    private void Prune()
    {
        try
        {
            if (!Directory.Exists(_dir)) return;
            var files = new DirectoryInfo(_dir).GetFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            foreach (var f in files.Skip(MaxCacheFiles)) f.Delete();
            foreach (var tmp in Directory.GetFiles(_dir, "*.tmp")) File.Delete(tmp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string RefKey(ToolEntry t) => $"{t.Kind}|{t.IconPath.Trim()}|{t.Path.Trim()}|{t.WorkingDirectory.Trim()}".ToUpperInvariant();
    private static string ImageKey(string path) => path.Trim().ToUpperInvariant();

    /// <summary>One background STA thread (shell icon APIs and WPF imaging want STA); created on first use.</summary>
    private sealed class StaWorker(string name)
    {
        private readonly BlockingCollection<Action> _queue = new();
        private Thread? _thread;
        private readonly object _gate = new();

        public Task<T> Run<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Start();
            _queue.Add(() =>
            {
                try { tcs.SetResult(work()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }

        private void Start()
        {
            if (_thread is not null) return;
            lock (_gate)
            {
                if (_thread is not null) return;
                var t = new Thread(() => { foreach (var job in _queue.GetConsumingEnumerable()) job(); })
                {
                    IsBackground = true,
                    Name = name,
                    Priority = ThreadPriority.BelowNormal,
                };
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                _thread = t;
            }
        }
    }
}

/// <summary>Large icons for programs, shortcuts and documents through the Windows shell.</summary>
internal static class ShellIcon
{
    public static BitmapSource? Get(string path, int size)
    {
        IntPtr hbitmap = IntPtr.Zero;
        IShellItemImageFactory? factory = null;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
            const int BiggerSizeOk = 0x1, IconOnly = 0x4; // icon, not a document thumbnail
            if (factory.GetImage(new NativeSize { Width = size, Height = size }, BiggerSizeOk | IconOnly, out hbitmap) != 0 || hbitmap == IntPtr.Zero)
                return null;
            return FromHBitmap(hbitmap);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null; // not found, no access, no shell handler…: the caller falls back
        }
        finally
        {
            if (hbitmap != IntPtr.Zero) DeleteObject(hbitmap);
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>Fallback: the classic 32 px associated icon.</summary>
    public static BitmapSource? Associated(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    /// <summary>
    /// Copies the bitmap's pixels with their alpha channel (Imaging.CreateBitmapSourceFromHBitmap drops it and leaves
    /// a black background).
    /// </summary>
    private static BitmapSource? FromHBitmap(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<NativeBitmap>(), out var bmp) == 0 || bmp.Width <= 0 || bmp.Height <= 0) return null;
        int w = bmp.Width, h = bmp.Height, stride = w * 4;
        var pixels = new byte[stride * h];
        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = w,
            Height = -h, // top-down rows
            Planes = 1,
            BitCount = 32,
        };
        var dc = CreateCompatibleDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hbitmap, 0, (uint)h, pixels, ref header, 0) == 0) return null;
        }
        finally { DeleteDC(dc); }

        // Icons without an alpha channel come back fully transparent: make them opaque instead.
        bool anyAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { anyAlpha = true; break; }
        var format = anyAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32;

        var src = BitmapSource.Create(w, h, 96, 96, format, null, pixels, stride);
        src.Freeze();
        return src;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width; public int Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type; public int Width; public int Height; public int WidthBytes;
        public ushort Planes; public ushort BitsPixel; public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size; public int Width; public int Height; public ushort Planes; public ushort BitCount;
        public uint Compression; public uint SizeImage; public int XPelsPerMeter; public int YPelsPerMeter;
        public uint ClrUsed; public uint ClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, out NativeBitmap bitmap);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
