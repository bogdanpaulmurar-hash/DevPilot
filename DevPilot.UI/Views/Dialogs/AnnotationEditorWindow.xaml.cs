// ============================================================
// DevPilot — IT Automation Hub v1.0
// AnnotationEditorWindow.xaml.cs — Annotation Editor code-behind
// Phase: 7
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DevPilot.UI.Views.Dialogs;

public partial class AnnotationEditorWindow : Window
{
    // ── State ─────────────────────────────────────────────────────────────
    private readonly string _sourceFilePath;
    private BitmapImage?    _baseImage;

    private enum Tool { Rectangle, Arrow, Text }
    private Tool _currentTool  = Tool.Rectangle;
    private Color _currentColor = Colors.Red;

    private System.Windows.Point _startPoint;
    private Shape? _currentShape;
    private readonly Stack<UIElement> _undoStack = new();

    // ── Constructor ───────────────────────────────────────────────────────
    public AnnotationEditorWindow(string filePath)
    {
        InitializeComponent();
        _sourceFilePath = filePath;
        LoadImage(filePath);
    }

    // ── Image loading ─────────────────────────────────────────────────────
    private void LoadImage(string path)
    {
        if (!File.Exists(path)) return;

        _baseImage = new BitmapImage(new Uri(path));
        BaseImage.Source        = _baseImage;
        AnnotationCanvas.Width  = _baseImage.PixelWidth;
        AnnotationCanvas.Height = _baseImage.PixelHeight;
        StatusLabel.Text        = $"Fisier: {System.IO.Path.GetFileName(path)}";
    }

    // ── Tool selection ────────────────────────────────────────────────────
    private void OnToolChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton btn) return;

        // Deselect all, then select clicked
        BtnRect.IsChecked  = btn == BtnRect;
        BtnArrow.IsChecked = btn == BtnArrow;
        BtnText.IsChecked  = btn == BtnText;

        _currentTool = btn.Name switch
        {
            "BtnArrow" => Tool.Arrow,
            "BtnText"  => Tool.Text,
            _          => Tool.Rectangle
        };
    }

    // ── Color picker ──────────────────────────────────────────────────────
    private void OnPickColor(object sender, MouseButtonEventArgs e)
    {
        var colors = new[]
        {
            Colors.Red, Colors.Yellow, Colors.LimeGreen,
            Colors.Cyan, Colors.DodgerBlue, Colors.Magenta
        };
        int idx = Array.IndexOf(colors, _currentColor);
        _currentColor = colors[(idx + 1) % colors.Length];
        ColorPreview.Background = new SolidColorBrush(_currentColor);
    }

    // ── Canvas mouse events ───────────────────────────────────────────────
    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(AnnotationCanvas);

        if (_currentTool == Tool.Text)
        {
            AddTextAnnotation(_startPoint);
            return;
        }

        _currentShape = _currentTool == Tool.Arrow
            ? CreateArrow(_startPoint, _startPoint)
            : CreateRect(_startPoint, _startPoint);

        AnnotationCanvas.Children.Add(_currentShape);
        _undoStack.Push(_currentShape);
        AnnotationCanvas.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_currentShape == null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(AnnotationCanvas);
        UpdateShape(_currentShape, _startPoint, pos);
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _currentShape = null;
        AnnotationCanvas.ReleaseMouseCapture();
    }

    // ── Shape factories ───────────────────────────────────────────────────
    private Rectangle CreateRect(System.Windows.Point a, System.Windows.Point b)
    {
        var r = new Rectangle
        {
            Stroke          = new SolidColorBrush(_currentColor),
            StrokeThickness = ThicknessSlider.Value,
            Fill            = Brushes.Transparent
        };
        UpdateShape(r, a, b);
        return r;
    }

    private Line CreateArrow(System.Windows.Point a, System.Windows.Point b)
        => new()
        {
            X1              = a.X,
            Y1              = a.Y,
            X2              = b.X,
            Y2              = b.Y,
            Stroke          = new SolidColorBrush(_currentColor),
            StrokeThickness = ThicknessSlider.Value
        };

    private static void UpdateShape(Shape shape, System.Windows.Point a, System.Windows.Point b)
    {
        if (shape is Rectangle r)
        {
            var x = Math.Min(a.X, b.X);
            var y = Math.Min(a.Y, b.Y);
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            r.Width  = Math.Abs(b.X - a.X);
            r.Height = Math.Abs(b.Y - a.Y);
        }
        else if (shape is Line l)
        {
            l.X2 = b.X;
            l.Y2 = b.Y;
        }
    }

    private void AddTextAnnotation(System.Windows.Point pos)
    {
        var tb = new TextBox
        {
            Background      = Brushes.Transparent,
            Foreground      = new SolidColorBrush(_currentColor),
            BorderThickness = new Thickness(0),
            FontSize        = 16,
            FontWeight      = FontWeights.Bold,
            Text            = "Text",
            CaretBrush      = new SolidColorBrush(_currentColor),
            MinWidth        = 60
        };
        Canvas.SetLeft(tb, pos.X);
        Canvas.SetTop(tb, pos.Y);
        AnnotationCanvas.Children.Add(tb);
        _undoStack.Push(tb);
        tb.Focus();
        tb.SelectAll();
    }

    // ── Toolbar actions ───────────────────────────────────────────────────
    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (_undoStack.TryPop(out var el))
            AnnotationCanvas.Children.Remove(el);
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        AnnotationCanvas.Children.Clear();
        _undoStack.Clear();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            var bmp = RenderToBitmap();
            var dir  = System.IO.Path.GetDirectoryName(_sourceFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var name = System.IO.Path.GetFileNameWithoutExtension(_sourceFilePath) + "_annotated.png";

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter           = "PNG Image|*.png",
                FileName         = name,
                InitialDirectory = dir
            };

            if (dlg.ShowDialog() != true) return;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var stream = System.IO.File.OpenWrite(dlg.FileName);
            encoder.Save(stream);

            StatusLabel.Text = $"Salvat: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Eroare la salvare: {ex.Message}", "DevPilot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCopyClipboard(object sender, RoutedEventArgs e)
    {
        try
        {
            var bmp = RenderToBitmap();
            Clipboard.SetImage(bmp);
            StatusLabel.Text = "Copiat in clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Eroare clipboard: {ex.Message}", "DevPilot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Render helper ─────────────────────────────────────────────────────
    private RenderTargetBitmap RenderToBitmap()
    {
        // Render the full Grid (image + annotations) at 96 DPI
        var target = new RenderTargetBitmap(
            (int)AnnotationCanvas.ActualWidth,
            (int)AnnotationCanvas.ActualHeight,
            96, 96, PixelFormats.Pbgra32);

        // Draw base image first
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (_baseImage != null)
                dc.DrawImage(_baseImage,
                    new Rect(0, 0, _baseImage.PixelWidth, _baseImage.PixelHeight));
        }
        target.Render(dv);

        // Draw annotations canvas on top
        target.Render(AnnotationCanvas);
        return target;
    }
}
