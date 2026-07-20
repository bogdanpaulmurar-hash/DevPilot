// ============================================================
// DevPilot — IT Automation Hub v1.0
// ScreenshotEntry.cs — Model pentru un screenshot capturat
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

namespace DevPilot.Models;

/// <summary>Reprezinta un screenshot capturat, stocat in istoricul serviciului.</summary>
public class ScreenshotEntry
{
    /// <summary>Calea completa catre fisierul PNG salvat pe disk.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Momentul capturii.</summary>
    public DateTime CapturedAt { get; set; } = DateTime.Now;

    /// <summary>Eticheta scurta (full / win).</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Numele fisierului (fara cale) — util in UI.</summary>
    public string FileName => System.IO.Path.GetFileName(FilePath);
}
