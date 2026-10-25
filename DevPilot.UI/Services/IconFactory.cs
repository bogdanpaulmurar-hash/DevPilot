using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DevPilot.Core.Hub;
using DevPilot.Core.Launch;
using DevPilot.Core.Presets;
using DevPilot.Models;
using DevPilot.UI.Converters;
using DevPilot.UI.Hub;
using DrawingIcon = System.Drawing.Icon;

namespace DevPilot.UI.Services;

/// <summary>Tray icons for tools: the user's .ico/.exe/.png, or the tool's symbol drawn on a rounded tile.</summary>
public static class IconFactory
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    public sealed class OwnedIcon(DrawingIcon icon, IntPtr handle) : IDisposable
    {
        public DrawingIcon Icon { get; } = icon;
        public void Dispose()
        {
            Icon.Dispose();
            if (handle != IntPtr.Zero) DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")] private static extern int GetDpiForSystem();

    /// <summary>Pixel size of a tray icon on this display (16 at 100%, 20 at 125%, 24 at 150%, 32 at 200% …).</summary>
    public static int TrayPixels()
    {
        try { return IconSizes.TrayPixels(GetDpiForSystem()); }
        catch (Exception) { return 16; }
    }

    /// <summary>DevPilot's own icon: the file chosen in Settings when it can be read, else the built-in one.</summary>
    public static OwnedIcon AppIcon(string? customPath = null)
    {
        var size = TrayPixels();
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            try { return FromFile(customPath, size); }
            catch (Exception) { /* unreadable file: built-in icon */ }
        }
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"));
        using var s = info!.Stream;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        ms.Position = 0;
        return FromBitmap(BestFrame(ms, size), size);
    }

    /// <summary>The same icon as a window icon (largest useful frame).</summary>
    public static BitmapSource? AppImage(string? customPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            {
                using var fs = new MemoryStream(File.ReadAllBytes(customPath));
                return BestFrame(fs, 32);
            }
        }
        catch (Exception) { /* built-in icon */ }
        return null;
    }

    public static OwnedIcon For(ToolEntry tool, string accentHex)
    {
        var size = TrayPixels();
        // Same order as the hub logo: picked file, manifest "icon", icon.ico next to the tool, the program's icon.
        var logo = LogoSource.Resolve(tool,
            string.IsNullOrWhiteSpace(tool.IconPath) ? ToolManifest.IconFor(tool) : null, File.Exists);
        if (!logo.IsNone && File.Exists(logo.Path))
        {
            try { return FromFile(logo.Path, size); }
            catch (Exception) { /* fall back to the glyph */ }
        }
        var color = !string.IsNullOrWhiteSpace(tool.Color) ? tool.Color : accentHex;
        return Glyph(FlyoutBuilder.GlyphOf(tool), color, size);
    }

    /// <summary>Icon of exactly <paramref name="size"/> px from an .ico (best frame), image or program, scaled with high quality.</summary>
    public static OwnedIcon FromFile(string path, int size)
    {
        BitmapSource? source = null;
        if (LogoSource.IsIcoFile(path) || LogoSource.IsImage(path))
        {
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            source = BestFrame(ms, size);
        }
        else
        {
            // Programs and shortcuts: ask the shell for a large icon, then scale down (ExtractAssociatedIcon is 32 px only).
            source = ShellIcon.Get(path, Math.Max(size, 64));
            if (source is null)
            {
                using var extracted = DrawingIcon.ExtractAssociatedIcon(path);
                if (extracted is not null)
                    source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(extracted.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
        }
        if (source is null) throw new InvalidOperationException("No icon in " + path);
        return FromBitmap(source, size);
    }

    private static BitmapSource BestFrame(Stream stream, int wanted)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frames = decoder.Frames;
        var sizes = frames.Select(f => f.PixelWidth).ToList();
        var frame = frames[Math.Max(0, IconSizes.PickFrame(sizes, wanted))];
        frame.Freeze();
        return frame;
    }

    /// <summary>Fits the image into a transparent size×size square (keeps proportions) and makes a tray icon of it.</summary>
    private static OwnedIcon FromBitmap(BitmapSource src, int size)
    {
        var (w, h) = LogoSource.FitInto(src.PixelWidth, src.PixelHeight, size);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
            dc.DrawImage(src, new Rect((size - w) / 2.0, (size - h) / 2.0, w, h));
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return ToOwned(rtb);
    }

    private static OwnedIcon ToOwned(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        using var bitmap = new System.Drawing.Bitmap(ms);
        var handle = bitmap.GetHicon();
        return new OwnedIcon(DrawingIcon.FromHandle(handle), handle);
    }

    public static OwnedIcon Glyph(string glyph, string backgroundHex, int size = 32)
    {
        if (!ColorUtil.TryParse(backgroundHex, out var bg)) bg = Color.FromRgb(0x4F, 0x8E, 0xF7);
        var fg = ColorUtil.Luminance(bg) > 0.45 ? Colors.Black : Colors.White;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(bg), null, new Rect(1, 1, size - 2, size - 2), size * 0.22, size * 0.22);
            var text = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                size * 0.56, new SolidColorBrush(fg), 1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return ToOwned(rtb);
    }
}
