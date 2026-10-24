using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DevPilot.Core.Config;
using DevPilot.Core.Interfaces;
using DevPilot.Core.Presets;
using DevPilot.Models;
using DevPilot.UI.Hub;
using DevPilot.UI.ViewModels;
using DevPilot.UI.Views;

namespace DevPilot.UI.Services;

/// <summary>
/// CI self-check (<c>DevPilot.exe --smoke &lt;folder&gt;</c>): seeds demo tools in a throw-away data folder,
/// visits every page, opens the hub and a flyout, saves screenshots and every binding error, then exits.
/// </summary>
public static class SmokeRunner
{
    private sealed class BindingErrorListener : TraceListener
    {
        public readonly StringBuilder Log = new();
        public int Count;
        public override void Write(string? message) => Log.Append(message);
        public override void WriteLine(string? message) { Count++; Log.AppendLine(message); }
    }

    private static readonly BindingErrorListener Errors = new();

    public static void Attach()
    {
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(Errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
    }

    public static async Task RunAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var report = new StringBuilder();
        int exit = 0;
        try
        {
            Seed();
            var shell = App.GetService<IAppShell>();
            var config = App.GetService<IConfigStore>();
            var main = App.GetService<MainViewModel>();

            shell.ShowMainWindow("hub");
            await Settle(1200);
            foreach (var item in main.Pages.Concat(main.FooterPages))
            {
                main.Navigate(item.Key);
                await Settle(700);
                Capture(FindMain(), Path.Combine(outDir, $"page-{item.Key}.png"));
                report.AppendLine($"page {item.Key}: ok");
            }

            // Tool editor with a folder tool and a group selected
            var tools = App.GetService<ToolsViewModel>();
            main.Navigate("tools");
            tools.Selected = tools.Items.OfType<ToolListEntry>().FirstOrDefault(t => t.Tool.IsFolderLike);
            await Settle(600);
            Capture(FindMain(), Path.Combine(outDir, "page-tools-folder.png"));
            tools.Selected = tools.Items.OfType<GroupListEntry>().FirstOrDefault();
            await Settle(600);
            Capture(FindMain(), Path.Combine(outDir, "page-tools-group.png"));

            // Hub
            shell.ShowHub(pinned: true);
            await Settle(1200);
            var hub = Application.Current.Windows.OfType<HubWindow>().First();
            Capture(hub, Path.Combine(outDir, "hub-dark.png"));
            report.AppendLine($"hub size: {hub.ActualWidth}x{hub.ActualHeight}");

            // Flyout of the first tool with options
            var vm = App.GetService<HubViewModel>();
            var tool = vm.Groups.SelectMany(g => g.Tools).FirstOrDefault(t => t.HasFlyout);
            if (tool is not null)
            {
                var panel = new FlyoutPanel(0, vm.Flyouts.ForTool(tool.Model), config.Current.Hub.FlyoutWidth, config.Current.Hub.FontSize, tool.Name, tool.StatusText);
                CaptureDetached(panel, Path.Combine(outDir, "flyout-tool.png"));
            }
            var folder = vm.Groups.SelectMany(g => g.Tools).FirstOrDefault(t => t.IsFolder);
            if (folder is not null)
            {
                var panel = new FlyoutPanel(0, vm.Flyouts.ForTool(folder.Model), config.Current.Hub.FlyoutWidth, config.Current.Hub.FontSize, folder.Name, null);
                CaptureDetached(panel, Path.Combine(outDir, "flyout-folder.png"));
            }
            if (vm.Spotlight is { } sp)
            {
                var panel = new FlyoutPanel(0, vm.Flyouts.ForTool(sp.Tool.Model), config.Current.Hub.FlyoutWidth, config.Current.Hub.FontSize, sp.Tool.Name, sp.Tool.StatusText);
                CaptureDetached(panel, Path.Combine(outDir, "flyout-spotlight.png"));
            }
            var quick = new FlyoutPanel(0, App.GetService<TrayManager>().QuickMenuItems(shell), 240, 13, "DevPilot", null);
            CaptureDetached(quick, Path.Combine(outDir, "quick-menu.png"));

            // Search
            vm.SearchText = "screen";
            await Settle(500);
            Capture(hub, Path.Combine(outDir, "hub-search.png"));
            vm.SearchText = string.Empty;

            // Light theme + wider hub + descriptions
            config.Current.Theme = ThemeMode.Light;
            config.Current.Hub.Width = 440;
            config.Current.Hub.ShowDescriptions = true;
            config.Current.Hub.Density = HubDensity.Spacious;
            config.Current.Accent = "#14A38B";
            config.Touch(ConfigArea.Theme);
            config.Touch(ConfigArea.Hub);
            await Settle(1200);
            Capture(hub, Path.Combine(outDir, "hub-light.png"));
            shell.HideHub();
            main.Navigate("hub");
            await Settle(800);
            Capture(FindMain(), Path.Combine(outDir, "page-hub-light.png"));
            main.Navigate("appearance");
            await Settle(800);
            Capture(FindMain(), Path.Combine(outDir, "page-appearance-light.png"));
        }
        catch (Exception ex)
        {
            exit = 1;
            report.AppendLine("FAILED: " + ex);
        }
        report.AppendLine($"binding errors: {Errors.Count}");
        File.WriteAllText(Path.Combine(outDir, "smoke-report.txt"), report.ToString());
        File.WriteAllText(Path.Combine(outDir, "binding-errors.txt"), Errors.Log.ToString());
        App.GetService<IAppShell>().Exit();
        Environment.Exit(exit);
    }

    private static MainWindow FindMain() => Application.Current.Windows.OfType<MainWindow>().First();

    private static async Task Settle(int ms)
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(ms);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Capture(Window w, string file)
    {
        if (w.Content is not FrameworkElement root) return;
        Save(root, root.ActualWidth, root.ActualHeight, file, opaque: !w.AllowsTransparency);
    }

    private static void CaptureDetached(FrameworkElement element, string file)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        Save(element, element.DesiredSize.Width, element.DesiredSize.Height, file, opaque: false);
    }

    private static void Save(Visual v, double width, double height, string file, bool opaque)
    {
        const double scale = 1.25;
        int pw = Math.Max(1, (int)Math.Ceiling(width * scale));
        int ph = Math.Max(1, (int)Math.Ceiling(height * scale));
        var rtb = new RenderTargetBitmap(pw, ph, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var bg = opaque ? (Brush)Application.Current.FindResource("WindowBgBrush") : new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
            dc.DrawRectangle(bg, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(v)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, width, height),
            }, null, new Rect(0, 0, width, height));
        }
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    /// <summary>Demo content resembling a real setup.</summary>
    private static void Seed()
    {
        var config = App.GetService<IConfigStore>();
        var s = config.Current;
        if (s.Tools.Count > 0) return;
        s.Groups.Clear();
        var work = new ToolGroup { Title = "Daily work", Order = 0, Color = "#4F8EF7" };
        var dev = new ToolGroup { Title = "Development", Order = 1, Color = "#A371F7", ColorStyle = RowColorStyle.Highlight, TextColor = "#A371F7" };
        var folders = new ToolGroup { Title = "Reference folders", Order = 2, Glyph = Glyphs.Folder };
        s.Groups.AddRange([work, dev, folders]);

        var sp = ToolPresets.Create(ToolPresets.ScreenPilot, @"C:\src\ScreenPilot\src", work.Id);
        var fp = ToolPresets.Create(ToolPresets.FocusPilot, @"C:\src\FocusPilot\FocusPilot.pyw", work.Id);
        fp.Color = "#3FB28C";
        fp.ColorStyle = RowColorStyle.MarkerAndHighlight;
        fp.GlyphColor = "#3FB28C";
        var rt = ToolPresets.Create(ToolPresets.RegTester, @"C:\src\regTester\regtester.py", work.Id);
        rt.Bold = true;
        var nx = ToolPresets.Create(ToolPresets.NexusX, @"C:\src\NexusX", dev.Id);
        nx.Tray = TrayMode.DevPilotIcon;
        var ap = ToolPresets.Create(ToolPresets.AutoPilot, @"C:\Tools\AutoPilot\AutoPilotDC.exe", dev.Id);
        var sep = new ToolEntry { Kind = ToolKind.Separator, SeparatorStyle = SeparatorStyle.Label, Name = "Utilities", Color = "#D9A441", GroupId = dev.Id };
        var np = new ToolEntry
        {
            Name = "Notepad", Kind = ToolKind.Executable, Path = @"%WINDIR%\System32\notepad.exe", GroupId = dev.Id,
            Description = "Quick notes", LabelMode = HubLabelMode.Auto, // logo taken from the program
        };
        var calc = new ToolEntry { Name = "Calculator", Kind = ToolKind.Executable, Path = @"%WINDIR%\System32\calc.exe", GroupId = dev.Id, LabelMode = HubLabelMode.LogoOnly };
        var winFolder = new ToolEntry
        {
            Name = "Windows folder", Kind = ToolKind.Folder, Path = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            GroupId = folders.Id, Folder = new FolderOptions { ExpandInHub = true, MaxItems = 12 },
        };
        var docs = new ToolEntry { Name = "Project wiki", Kind = ToolKind.Url, Path = "https://github.com/", GroupId = folders.Id, Italic = true, LabelMode = HubLabelMode.TextOnly };
        s.Tools.AddRange([sp, fp, rt, nx, ap, sep, np, calc, winFolder, docs]);
        Normalizer.Normalize(s);
        config.Touch(ConfigArea.All);
    }
}
