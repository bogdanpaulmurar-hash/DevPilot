// ============================================================
// DevPilot — IT Automation Hub v1.0
// IScreenshotService.cs — Contract pentru Screenshot Tool
// Phase: 7
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

/// <summary>
/// Contract pentru captura de ecran si gestionarea istoricului.
/// </summary>
public interface IScreenshotService : IDisposable
{
    /// <summary>Captureaza tot ecranul principal. Returneaza calea fisierului salvat.</summary>
    Task<string> CaptureFullScreenAsync(CancellationToken ct = default);

    /// <summary>Captureaza fereastra activa. Returneaza calea fisierului salvat.</summary>
    Task<string> CaptureActiveWindowAsync(CancellationToken ct = default);

    /// <summary>Copiaza ultimul screenshot in clipboard.</summary>
    void CopyLastToClipboard();

    /// <summary>Ultimele 10 screenshot-uri (cel mai recent primul).</summary>
    IReadOnlyList<ScreenshotEntry> History { get; }

    /// <summary>Folder de output configurabil.</summary>
    string OutputFolder { get; set; }
}
