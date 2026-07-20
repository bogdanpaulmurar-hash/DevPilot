// ============================================================
// DevPilot — IT Automation Hub v1.0
// TrayService.cs — Programmatic tray context-menu builder
// Phase: 17 (sidebar teal cu titlu rotit, NO section headers,
//             per-app TrayFontStyle, HideJigglerInTray support,
//             NO icon column gap — BuildNoIconMenuItemTemplate)
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using DevPilot.Core.Interfaces;
using DevPilot.Models;
using DevPilot.UI.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DevPilot.UI.Services;

/// <summary>
/// Builds the tray ContextMenu programmatically so that app-launch shortcuts
/// can be added / removed at runtime without restarting the application.
/// </summary>
public sealed class TrayService : ITrayService
{
    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly ISettingsManager _settings;
    private readonly ILauncherService _launcher;
    private readonly TrayViewModel _trayVm;
    private readonly ILogger<TrayService> _logger;

    private TaskbarIcon? _icon;

    // Teal sidebar color — Siemens primary
    private static readonly SolidColorBrush SidebarBrush =
        new(Color.FromRgb(0x00, 0x99, 0x99));

    // Section display order for grouped tray entries
    private static readonly AppEntryType[] SectionOrder =
    [
        AppEntryType.Folder,
        AppEntryType.Executable,
        AppEntryType.Python,
        AppEntryType.PowerShell,
        AppEntryType.Batch,
        AppEntryType.Url,
        AppEntryType.Document,
        AppEntryType.NetworkShare,
        AppEntryType.Ssh,
        AppEntryType.Wsl,
        AppEntryType.ShellCommand,
        AppEntryType.VBScript,
    ];

    // ── Constructor ───────────────────────────────────────────────────────────

    public TrayService(
        ISettingsManager settings,
        ILauncherService launcher,
        TrayViewModel trayVm,
        ILogger<TrayService> logger)
    {
        _settings = settings;
        _launcher = launcher;
        _trayVm = trayVm;
        _logger = logger;
    }

    // ── Startup ───────────────────────────────────────────────────────────────

    public void Initialize(TaskbarIcon icon)
    {
        _icon = icon;
        RebuildMenu();
    }

    // ── ITrayService ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void RebuildMenu()
    {
        if (_icon is null) return;

        if (Application.Current.Dispatcher.CheckAccess())
            BuildContextMenu();
        else
            Application.Current.Dispatcher.BeginInvoke(BuildContextMenu);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
        var s = _settings.Current;

        // ── Inject custom Template cu sidebar teal ────────────────────────────
        menu.Template = BuildMenuTemplate(s.TraySidebarWidth);
        menu.Padding = new Thickness(0);
        menu.HasDropShadow = false; // shadow-ul e deja pe rootBorder, evitam dublu spatiu

        // ── Elimina coloana de icon din toate MenuItem-urile (gap-ul alb) ─────
        // IMPORTANT: TargetName in Trigger.Setter NU functioneaza cu
        // FrameworkElementFactory in code-behind (arunca InvalidOperationException).
        // Solutia: Trigger fara TargetName — se aplica direct pe MenuItem.
        var noIconTemplate = BuildNoIconMenuItemTemplate();
        var noIconStyle = new Style(typeof(MenuItem));
        noIconStyle.Setters.Add(new Setter(MenuItem.TemplateProperty, noIconTemplate));

        // Hover teal via Style.Trigger (fara TargetName — functioneaza in code-behind)
        var hoverTrigger = new Trigger
        {
            Property = MenuItem.IsHighlightedProperty,
            Value = true
        };
        hoverTrigger.Setters.Add(new Setter(
            MenuItem.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x99, 0x99))));
        noIconStyle.Triggers.Add(hoverTrigger);

        // Disabled — text gri
        var disabledTrigger = new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false
        };
        disabledTrigger.Setters.Add(new Setter(
            MenuItem.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA))));
        noIconStyle.Triggers.Add(disabledTrigger);

        menu.Resources[typeof(MenuItem)] = noIconStyle;

        // ── Show main window ──────────────────────────────────────────────────
        menu.Items.Add(new MenuItem
        {
            Header = "Show DevPilot",
            Command = _trayVm.ShowWindowCommand
        });

        // ── App shortcuts (ShowInTray == true, max 20, grouped by type) ───────
        var shortcuts = s.Apps
            .Where(a => a.ShowInTray)
            .Take(20)
            .ToList();

        if (shortcuts.Count > 0)
        {
            var groups = shortcuts
                .GroupBy(a => a.Type)
                .OrderBy(g =>
                {
                    var idx = Array.IndexOf(SectionOrder, g.Key);
                    return idx >= 0 ? idx : 99;
                })
                .ToList();

            foreach (var group in groups)
            {
                // Lookup appearance config for this type
                s.TrayTypeConfigs.TryGetValue(group.Key.ToString(), out var typeCfg);
                var typeEmoji = typeCfg?.Emoji ?? string.Empty;
                var typeColorBrush = TryParseBrush(typeCfg?.Color);

                // Separator intre grupuri — FARA section header
                menu.Items.Add(new Separator());

                // ── Entries in this group ──────────────────────────────────────
                foreach (var app in group)
                {
                    var captured = app;

                    var appEmoji = string.IsNullOrWhiteSpace(captured.TagEmoji)
                        ? typeEmoji
                        : captured.TagEmoji;

                    var appBrush = string.IsNullOrWhiteSpace(captured.TagColor)
                        ? typeColorBrush
                        : TryParseBrush(captured.TagColor);

                    var label = string.IsNullOrEmpty(appEmoji)
                        ? captured.Name
                        : $"{appEmoji} {captured.Name}";

                    var item = new MenuItem { Header = label };

                    if (appBrush is not null)
                        item.Foreground = appBrush;

                    ApplyFontStyle(item, captured.TrayFontStyle);

                    item.Click += (_, _) => _ = SafeLaunchAsync(captured);
                    menu.Items.Add(item);
                }
            }
        }

        // ── Jiggler toggle (optional — hidden via settings) ───────────────────
        if (!s.HideJigglerInTray)
        {
            menu.Items.Add(new Separator());
            var jigglerItem = new MenuItem();
            BindingOperations.SetBinding(
                jigglerItem,
                HeaderedItemsControl.HeaderProperty,
                new Binding(nameof(TrayViewModel.JigglerMenuLabel)) { Source = _trayVm });
            jigglerItem.Command = _trayVm.ToggleJigglerCommand;
            menu.Items.Add(jigglerItem);
        }

        // ── Exit ──────────────────────────────────────────────────────────────
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = "Exit DevPilot",
            Command = _trayVm.ExitCommand
        });

        _icon!.ContextMenu = menu;
        _logger.LogDebug(
            "Tray menu rebuilt — {Count} shortcut(s), {Groups} group(s), jiggler hidden: {Hidden}.",
            shortcuts.Count,
            shortcuts.GroupBy(a => a.Type).Count(),
            s.HideJigglerInTray);
    }

    /// <summary>
    /// Aplica FontWeight, FontStyle sau TextDecorations pe un MenuItem
    /// in functie de valoarea TrayFontStyle din AppEntry.
    /// </summary>
    private static void ApplyFontStyle(MenuItem item, string? style)
    {
        switch (style)
        {
            case "Bold":
                item.FontWeight = FontWeights.Bold;
                break;
            case "SemiBold":
                item.FontWeight = FontWeights.SemiBold;
                break;
            case "Italic":
                item.FontStyle = FontStyles.Italic;
                break;
            case "Strikethrough":
                // Strikethrough necesita TextBlock ca Header
                var tb = new TextBlock
                {
                    Text = item.Header as string ?? string.Empty,
                    TextDecorations = TextDecorations.Strikethrough
                };
                item.Header = tb;
                break;
            default: // "Normal" sau null
                item.FontWeight = FontWeights.Normal;
                item.FontStyle = FontStyles.Normal;
                break;
        }
    }

    /// <summary>
    /// Construieste un ControlTemplate pentru ContextMenu cu sidebar teal pe stanga
    /// si titlul rotit 270 de grade.
    /// </summary>
    private static ControlTemplate BuildMenuTemplate(int sidebarWidth = 26)
    {
        var template = new ControlTemplate(typeof(ContextMenu));

        // ── Root Border (shadow) ──────────────────────────────────────────────
        var rootBorderFactory = new FrameworkElementFactory(typeof(Border));
        rootBorderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);
        rootBorderFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        rootBorderFactory.SetValue(Border.PaddingProperty, new Thickness(0));
        rootBorderFactory.SetValue(UIElement.EffectProperty, new DropShadowEffect
        {
            BlurRadius = 12,
            Opacity = 0.35,
            ShadowDepth = 3,
            Direction = 270,
            Color = Colors.Black
        });

        // ── Inner Border (background + border) ───────────────────────────────
        var innerBorderFactory = new FrameworkElementFactory(typeof(Border));
        innerBorderFactory.SetValue(Border.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)));
        innerBorderFactory.SetValue(Border.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));
        innerBorderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        innerBorderFactory.SetValue(Border.PaddingProperty, new Thickness(0));

        // ── Grid: col 0 = sidebar teal, col 1 = items ────────────────────────
        var gridFactory = new FrameworkElementFactory(typeof(Grid));

        var col0 = new FrameworkElementFactory(typeof(ColumnDefinition));
        col0.SetValue(ColumnDefinition.WidthProperty, new GridLength(sidebarWidth));
        var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
        col1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
        gridFactory.AppendChild(col0);
        gridFactory.AppendChild(col1);

        // ── Sidebar teal ──────────────────────────────────────────────────────
        var sidebarFactory = new FrameworkElementFactory(typeof(Border));
        sidebarFactory.SetValue(Grid.ColumnProperty, 0);
        sidebarFactory.SetValue(Border.BackgroundProperty, SidebarBrush);

        // TextBlock cu titlul rotit 270 grade
        var sidebarTextFactory = new FrameworkElementFactory(typeof(TextBlock));
        sidebarTextFactory.SetValue(TextBlock.TextProperty, "DevPilot — Bogdan's Toolkit Hub");
        sidebarTextFactory.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        sidebarTextFactory.SetValue(TextBlock.FontSizeProperty, 13.0);
        sidebarTextFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        sidebarTextFactory.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI"));
        sidebarTextFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        sidebarTextFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        sidebarTextFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        sidebarTextFactory.SetValue(FrameworkElement.LayoutTransformProperty, new RotateTransform(270));

        sidebarFactory.AppendChild(sidebarTextFactory);

        // ── ItemsPresenter (col 1) ────────────────────────────────────────────
        var itemsPresenterFactory = new FrameworkElementFactory(typeof(ItemsPresenter));
        itemsPresenterFactory.SetValue(Grid.ColumnProperty, 1);
        itemsPresenterFactory.SetValue(FrameworkElement.NameProperty, "ItemsPresenter");
        itemsPresenterFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0));

        // Asamblare
        gridFactory.AppendChild(sidebarFactory);
        gridFactory.AppendChild(itemsPresenterFactory);
        innerBorderFactory.AppendChild(gridFactory);
        rootBorderFactory.AppendChild(innerBorderFactory);

        template.VisualTree = rootBorderFactory;
        return template;
    }

    /// <summary>
    /// ControlTemplate pentru MenuItem fara coloana de icon.
    /// Elimina gap-ul alb dintre sidebar si textul item-ului.
    /// Emoji-ul din Header (text) ramane intact.
    ///
    /// NOTA: NU folosim Trigger cu TargetName in code-behind cu FrameworkElementFactory —
    /// WPF arunca InvalidOperationException ("Child with Name not found in VisualTree").
    /// Hover-ul este gestionat via Style.Trigger in BuildContextMenu (fara TargetName).
    /// </summary>
    private static ControlTemplate BuildNoIconMenuItemTemplate()
    {
        var template = new ControlTemplate(typeof(MenuItem));

        // Root Border — fara coloana de icon, padding compact
        // Background-ul de hover este setat prin Style.Trigger din BuildContextMenu
        // si se aplica direct pe MenuItem (nu pe Border cu TargetName).
        var rootBorder = new FrameworkElementFactory(typeof(Border));
        rootBorder.SetValue(Border.SnapsToDevicePixelsProperty, true);
        rootBorder.SetValue(Border.PaddingProperty, new Thickness(14, 5, 18, 5));

        // Binding Background la MenuItem.Background (controlat de Style.Trigger)
        rootBorder.SetBinding(Border.BackgroundProperty,
            new Binding(nameof(MenuItem.Background))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });

        // ContentPresenter — afiseaza Header-ul (text + emoji)
        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        contentPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        contentPresenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);

        rootBorder.AppendChild(contentPresenter);
        template.VisualTree = rootBorder;

        // NU adaugam Trigger cu TargetName — nu functioneaza cu FrameworkElementFactory!
        // Trigger-ele sunt in Style-ul din BuildContextMenu.

        return template;
    }

    private static Brush? TryParseBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(c);
        }
        catch { return null; }
    }

    private async Task SafeLaunchAsync(AppEntry app)
    {
        try
        {
            await _launcher.LaunchAsync(app);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray shortcut launch failed for '{App}'.", app.Name);
        }
    }
}
