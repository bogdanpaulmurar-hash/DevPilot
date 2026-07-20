// ============================================================
// DevPilot — IT Automation Hub v1.0
// ScreenshotService.cs — Screenshot capture implementation
// Phase: 7/8
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using DevPilot.Core.Interfaces;
using DevPilot.Models;
using Microsoft.Extensions.Logging;

namespace DevPilot.Infrastructure.Services;

/// <summary>
/// Captureaza ecranul (full / fereastra activa) via GDI+ si gestioneaza
/// istoricul ultimelor 10 screenshot-uri.
/// </summary>
public sealed class ScreenshotService : IScreenshotService
{
    // ── Win32 ─────────────────────────────────────────────────────────────
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    // ── State ─────────────────────────────────────────────────────────────
    private readonly ILogger<ScreenshotService> _logger;
    private readonly List<ScreenshotEntry>      _history = [];
    private const int MaxHistory = 10;
    private string _lastFilePath = string.Empty;

    /// <summary>Folder unde se salveaza screenshot-urile. Configurabil din UI.</summary>
    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DevPilot");

    /// <summary>Ultimele 10 screenshot-uri (cel mai recent primul).</summary>
    public IReadOnlyList<ScreenshotEntry> History => _history.AsReadOnly();

    // ── Constructor ───────────────────────────────────────────────────────
    public ScreenshotService(ILogger<ScreenshotService> logger)
    {
        _logger = logger;
    }

    // ── API ───────────────────────────────────────────────────────────────

    public Task<string> CaptureFullScreenAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            EnsureOutputFolder();
            var bounds = GetVirtualScreenBounds();
            var path   = BuildPath("full");

            using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

            bmp.Save(path, ImageFormat.Png);
            AddToHistory(path, "full");
            _logger.LogInformation("Full-screen screenshot saved: {Path}", path);
            return Task.FromResult(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CaptureFullScreenAsync failed.");
            return Task.FromResult(string.Empty);
        }
    }

    public Task<string> CaptureActiveWindowAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            EnsureOutputFolder();
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
                return CaptureFullScreenAsync(ct);

            int w = rect.Right  - rect.Left;
            int h = rect.Bottom - rect.Top;
            if (w <= 0 || h <= 0) return CaptureFullScreenAsync(ct);

            var path = BuildPath("win");
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);

            bmp.Save(path, ImageFormat.Png);
            AddToHistory(path, "win");
            _logger.LogInformation("Active-window screenshot saved: {Path}", path);
            return Task.FromResult(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CaptureActiveWindowAsync failed.");
            return Task.FromResult(string.Empty);
        }
    }

    public void CopyLastToClipboard()
    {
        if (string.IsNullOrEmpty(_lastFilePath))
        {
            _logger.LogWarning("CopyLastToClipboard: no screenshot available.");
            return;
        }
        try
        {
            var img = new BitmapImage(new Uri(_lastFilePath));
            System.Windows.Clipboard.SetImage(img);
            _logger.LogInformation("Screenshot copied to clipboard: {Path}", _lastFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CopyLastToClipboard failed.");
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────
    public void Dispose() { /* GDI+ resources released via using blocks */ }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void EnsureOutputFolder()
    {
        if (!Directory.Exists(OutputFolder))
            Directory.CreateDirectory(OutputFolder);
    }

    private string BuildPath(string tag)
        => Path.Combine(OutputFolder,
            $"DevPilot_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.png");

    private void AddToHistory(string path, string tag)
    {
        _lastFilePath = path;
        _history.Insert(0, new ScreenshotEntry
        {
            FilePath   = path,
            CapturedAt = DateTime.Now,
            Tag        = tag
        });
        if (_history.Count > MaxHistory)
            _history.RemoveAt(_history.Count - 1);
    }

    private static System.Drawing.Rectangle GetVirtualScreenBounds()
        => new(
            (int)SystemParameters.VirtualScreenLeft,
            (int)SystemParameters.VirtualScreenTop,
            (int)SystemParameters.VirtualScreenWidth,
            (int)SystemParameters.VirtualScreenHeight);
}
