// ============================================================
// DevPilot — IT Automation Hub v1.0
// HeartbeatStatus.cs — Status enum pentru Heartbeat Monitor
// Phase: 8
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

namespace DevPilot.Models;

public enum HeartbeatStatus
{
    Unknown,   // ⚠️ not yet checked
    Alive,     // ✅ process running / URL returned 2xx
    Dead       // ❌ process not found / URL failed
}
