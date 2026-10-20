using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DevPilot.Core.Launch;
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

    public static DrawingIcon AppIcon()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"));
        using var s = info!.Stream;
        return new DrawingIcon(s);
    }

    public static OwnedIcon For(ToolEntry tool, string accentHex)
    {
        var path = PathTokens.Expand(tool.IconPath, tool).Trim('"');
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".ico") return new OwnedIcon(new DrawingIcon(path, 32, 32), IntPtr.Zero);
                if (ext is ".exe" or ".dll")
                {
                    var extracted = DrawingIcon.ExtractAssociatedIcon(path);
                    if (extracted is not null) return new OwnedIcon(extracted, IntPtr.Zero);
                }
                if (ext is ".png" or ".jpg" or ".bmp")
                {
                    using var bmp = new System.Drawing.Bitmap(path);
                    using var small = new System.Drawing.Bitmap(bmp, 32, 32);
                    var h = small.GetHicon();
                    return new OwnedIcon(DrawingIcon.FromHandle(h), h);
                }
            }
            catch (Exception) { /* fall back to the glyph */ }
        }
        var color = !string.IsNullOrWhiteSpace(tool.Color) ? tool.Color : accentHex;
        return Glyph(FlyoutBuilder.GlyphOf(tool), color);
    }

    public static OwnedIcon Glyph(string glyph, string backgroundHex)
    {
        const int size = 32;
        if (!ColorUtil.TryParse(backgroundHex, out var bg)) bg = Color.FromRgb(0x4F, 0x8E, 0xF7);
        var fg = ColorUtil.Luminance(bg) > 0.45 ? Colors.Black : Colors.White;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(bg), null, new Rect(1, 1, size - 2, size - 2), 7, 7);
            var text = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                18, new SolidColorBrush(fg), 1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        using var bitmap = new System.Drawing.Bitmap(ms);
        var handle = bitmap.GetHicon();
        return new OwnedIcon(DrawingIcon.FromHandle(handle), handle);
    }
}
