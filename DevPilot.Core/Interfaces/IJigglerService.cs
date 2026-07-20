// ============================================================
// DevPilot — IT Automation Hub v1.0
// IJigglerService.cs — Contract pentru Mouse Jiggler
// Autor: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

namespace DevPilot.Core.Interfaces;

public interface IJigglerService : IDisposable
{
    bool IsRunning { get; }
    void Start(int intervalSeconds);
    void Stop();
}
