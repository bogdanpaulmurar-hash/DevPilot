# Prompt: build the Egress Guard in DevPilot (personal environment)

Copy everything below the line into a Claude session that has the DevPilot repo.

---

You are working on DevPilot (C# WPF, .NET 8). Read `CLAUDE.md` first and follow it.

## Ground rules for this session
- This is a personal environment. **Do not ask me for anything about an employer, a company policy, internal names, e-mail addresses, IDs, hosts or numbers.** If a step seems to need one, use a placeholder and keep going (see "Policy data stays outside the repo").
- Code, comments, commits and docs: English. Replies to me: Romanian, short, phone-friendly.
- Logic goes in `DevPilot.Core` so it is testable without WPF. No `.exe` files added to the repo.
- One small PR per step below, in order. Each PR: build + tests green, `CHANGELOG.md` updated, version bumped in `DevPilot.UI.csproj` only when the PR is user-visible. Stop after each PR and wait for my "go" before the next. Plan in 3-5 lines first.
- Do not start items from the MODIFICARI document unless I ask.

## Goal
An **Egress Guard**: an opt-in feature that watches the outbound network activity of the tools DevPilot launches and checks it against a user-supplied policy file (IT and AI-use rules). It must be useful to anyone, not tied to one organisation. Default: **off**, observe-only.

## Policy data stays outside the repo
- The repo ships only the **schema**, a generic **sample** policy (`example.com`-style hosts) and the engine.
- The real policy file is loaded from a path set in Settings (or `DEVPILOT_EGRESS_POLICY`). Nothing from it is copied into logs, docs, tests or the config backup (only the path is).
- Anything that depends on real policy content is a **work item for the work-side Claude**: record it in `docs/WORK_HANDOFF.md` (create it in PR 11) as a checklist line, do not guess it.

## The 11 PRs (keep this order)
1. **Settings model** - `EgressGuardSettings` in `DevPilot.Models` (enabled=false, mode=Observe, policy path, log folder, retention), wired into config with defaults and a migration test.
2. **Policy schema + loader** - `Core/Egress/PolicyFile`: versioned JSON, strict validation with readable errors, sample file in `docs/`. No network.
3. **Rule evaluator** - pure function: (tool, process, host, port, protocol, category) -> Allow / Warn / Deny + matched rule id. Wildcards, precedence, default action. Heavy unit tests.
4. **Categories** - `Core/Egress/Categories`: classify destinations (for example "ai-service", "storage", "unknown") from the policy file only; nothing hardcoded per vendor.
5. **Audit log** - metadata only (time, tool, process, host, port, decision, rule id). Never payloads, never URLs with query strings. Rotation, retention, export to CSV. Core writer + Infrastructure file sink.
6. **Connection sampler** - `Infrastructure`: enumerate TCP connections per managed process tree (IP Helper table), map to the tool that DevPilot launched, resolve host names with a cache. Polling, low overhead, no admin rights, no driver.
7. **Guard service** - ties 3-6 together: runs only for managed tools, Observe and Warn modes. Fails open and says so when the policy is missing or invalid (clear status, never a silent stop).
8. **Tool contract** - optional `egress` block in `devpilot.tool.json` (expected hosts, purpose), backward compatible; new env var `DEVPILOT_EGRESS_GUARD=1` for managed tools. Update `docs/INTEGRATION.md`.
9. **Hub status** - tray/hub indicator and per-tool badge (off / ok / warning), colours from existing theme settings, no per-tool special cases.
10. **Egress page** - main-window page: live connections, last decisions, filter, CSV export, "policy loaded from..." status and a self-check button.
11. **Docs, backup, handoff** - `docs/EGRESS.md`, backup includes settings (not the policy file or logs), `docs/WORK_HANDOFF.md` checklist, `CLAUDE.md` update (map + rules).

Block mode (terminating a process or touching the firewall) is **out of scope** until I ask for it separately.

## Definition of done per PR
Build and tests green in CI, no new warnings, no secrets, no employer-specific text, `CHANGELOG.md` entry, short Romanian summary: what was done, what is left, next step.
