# Prompt for the personal Claude (paste as-is)

You are working on DevPilot (C# WPF, .NET 8, Windows tray hub). Read `CLAUDE.md` and
`docs/claudepolicy.md` first. Replies to me in Romanian, short; code, comments,
commits, docs in English. Work in small steps, run `dotnet test DevPilot.Tests`
after each, note changes in CHANGELOG.md, bump `<Version>` in
`DevPilot.UI/DevPilot.UI.csproj`, use a feature branch + PR.

## Goal
Add **Egress Guard**: a feature that checks what a tool could send out of the machine
and lets me limit or stop it. DevPilot manages tools that I build privately and run
on a managed work laptop, so I want visibility and control over their outbound data
flow. Build it as a generic security feature. Do not assume anything about the
laptop's network or any employer; do not ask me for such details.

## Hard constraints
- No employer names, internal hosts, IDs, e-mails or policy text anywhere (code,
  tests, docs, samples, commits). Use placeholders such as `example.internal`.
- Deny-list terms and company-specific rules are NOT in the repo. Load them at
  runtime from `%APPDATA%\DevPilot\policy.local.json` (optional; absent = empty;
  never logged, never copied into reports, never part of backups/exports).
- Never bypass, disable, hide from or tamper with any OS/endpoint security tool.
  No admin-only tricks by default. Anything needing elevation is optional,
  off by default, and clearly labelled.
- Backward compatible with existing `devpilot.tool.json` files (ScreenPilot,
  AutoMailCAT, AutoTemplates). New data goes in an optional `network` block.
- Logic in `DevPilot.Core` (testable without WPF); Windows APIs in
  `DevPilot.Infrastructure`; UI in `DevPilot.UI`. Nothing hardcoded per tool.
- No telemetry, no network calls made by DevPilot itself for this feature.

## Components (implement in this order, one PR each)
1. **Core/Guard model + policy**: `EgressMode {Off, Monitor, Block}`, `ToolNetworkPolicy`
   (allowed hosts with wildcard, ports, `allowNone`), `GuardFinding` (severity
   Info/Warn/High, rule id, file, line, message), `GuardReport`. Parse the optional
   `network` block of `devpilot.tool.json` and the optional local policy file.
2. **Static scanner** (`Core/Guard/ToolScanner`, extends the idea of
   `Detection/ProjectScanner`): scan a tool folder (C#, Python, JS/TS, PowerShell,
   batch, config) for: HTTP/socket/WebSocket/FTP/SMTP clients, hard-coded URLs and
   IPs, telemetry/analytics SDKs, auto-update code, file/cloud upload, clipboard /
   screenshot / keystroke capture, process spawning of shells, registry Run keys and
   scheduled tasks, AI/LLM API clients and keys, embedded secret patterns, and
   local deny-list terms. Skip `bin`, `obj`, `node_modules`, `.git`. Rules are data
   (JSON), not code, so they can be extended. Unit tests with fixture snippets.
3. **Provenance + drift**: record git remote and HEAD (read from `.git` files, no
   git.exe needed), plus a SHA-256 manifest of tool files at approval time. Detect
   change since approval and mark the tool "needs re-review". Store approvals in
   the DevPilot config (hash, date, mode, allowlist), not in the tool folder.
4. **Approval gate**: a tool is `Unreviewed`, `Approved`, or `Blocked`. In `Block`
   mode only `Approved` tools with an unchanged manifest start; in `Monitor` mode
   everything starts but findings and warnings are shown and logged.
5. **Runtime monitor** (Infrastructure): poll per-process TCP/UDP endpoints for
   managed process trees (IP Helper `GetExtendedTcpTable`/`GetExtendedUdpTable`, no
   packet capture, no driver). Compare with the tool's allowlist (resolve names
   through reverse/cached DNS where possible; unknown = flagged). Actions by mode:
   log, tray notification, or stop the process tree. Rate-limit notifications.
6. **Soft offline mode**: launch a tool with `HTTP_PROXY/HTTPS_PROXY/ALL_PROXY` set
   to a dead local address and `NO_PROXY` empty; document that it only affects
   tools that honour proxy variables and is not a security boundary.
7. **Kill switch**: tray menu item "Pause external access": stops monitored
   network-capable tools or refuses new starts until resumed; state persisted.
8. **UI**: a "Guard" page in the main window: mode selector, per-tool status
   (Unreviewed/Approved/Blocked/Drifted), findings list with severity, allowlist
   editor, last connections per tool, "Scan now", "Approve", "Export report"
   (report contains rule ids and counts, redacts matched deny-list terms).
9. **Pre-flight text check**: a small Core service plus tray action that checks the
   clipboard or a file for deny-list terms and common secret/PII patterns before I
   paste it into an AI tool; shows only the category and position, never the term.
10. **Repo hygiene**: add `backups/` to `.gitignore`; add a CI step (GitHub Actions)
    that fails on generic secret patterns; add a pre-commit hook script under
    `scripts/` that runs the same check locally plus the local deny-list when
    the file exists.
11. **Docs**: `docs/GUARD.md` (how it works, limits, modes), extend
    `docs/INTEGRATION.md` with the `network` block, update `CLAUDE.md` map and rules
    (keep under ~60 lines), CHANGELOG.

## Design notes
- Honest limits belong in the UI and docs: DevPilot only sees processes it launches;
  it is an audit and guard-rail layer, not a replacement for organisational controls.
- Defaults: mode `Monitor`, local-only logging with sensitive matches redacted,
  logs in `%LOCALAPPDATA%\DevPilot\logs`.
- Keep the scanner conservative: report, explain, never auto-delete or modify tools.
- Each finding has a short "why it matters" and "how to fix" text (editor Help
  style, see `Core/Help`).

## Definition of done
Tests green, CI green, version bumped, CHANGELOG and docs updated, PR opened.
At the end give me a 10-line summary in Romanian: what was added, what is
Windows-only unverified, and what remains. Do not ask me for any work-environment
information; if something depends on it, expose it as a setting.
