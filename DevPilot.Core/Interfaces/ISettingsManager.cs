// ============================================================
// DevPilot — IT Automation Hub v1.0
// ISettingsManager.cs — Contract pentru Settings
// Autor: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using DevPilot.Models;

namespace DevPilot.Core.Interfaces;

public interface ISettingsManager
{
    AppSettings Current { get; }
    void Load();
    void Save();
    void Reset();
}
