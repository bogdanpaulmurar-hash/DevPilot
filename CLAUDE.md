# DevPilot — notes for Claude

Windows hub (C# WPF, .NET 8) that starts, groups and controls the user's other tools from one tray icon.

## Commands
- Build: `dotnet build DevPilot.sln -c Release`
- Tests: `dotnet test DevPilot.Tests` (xUnit; WPF/Windows only — on Linux rely on GitHub Actions)
- Install on a PC: `scripts\install.ps1 -StartWithWindows`, or `scripts\install.cmd` where PowerShell scripts are blocked
- Version: `<Version>` in `DevPilot.UI/DevPilot.UI.csproj` (bump on every release)

## Map
- `DevPilot.Models` — config and tool models
- `DevPilot.Core` — logic without Windows UI: Config, Hub, Folders, Launch, Presets, KeepAwake, Startup
- `DevPilot.Infrastructure` — Windows services: config store, launcher, process monitor, keep-awake, startup manager, hotkeys
- `DevPilot.UI` — WPF: tray hub, flyouts, main window pages (Views / ViewModels / Themes)
- `docs/INSTALL.md`, `docs/INTEGRATION.md` (how a tool becomes DevPilot-aware)
- Config: `%APPDATA%\DevPilot\config.json`; log: `%LOCALAPPDATA%\DevPilot\logs\devpilot.log`

## Rules
- DevPilot is the single owner of keep-awake and start-with-Windows. Managed tools get `DEVPILOT_MANAGED=1` and `DEVPILOT_SUPPRESS=tray,keepawake,autostart`; never break a tool that runs without DevPilot.
- Tools describe their options in an optional `devpilot.tool.json` (see INTEGRATION.md). Keep the format backward compatible: ScreenPilot, AutoMailCAT and AutoTemplates already ship one.
- Settings over special cases: groups, colours, symbols and sizes are configurable, nothing hardcoded per tool (ScreenPilot's spotlight card is the only deliberate exception).
- Keep logic in Core so it stays testable without WPF.
- `v1-legacy` is the old version; `ci-shots` holds CI UI screenshots. Work on `main` or a feature branch + PR.
- Code, comments, commits and docs in English; replies to the user in Romanian, short.
- Visible docs: never the employer's name, company e-mails, internal IDs or numbers.

## Shared context
Project overview and open bugs/changes live in two Google Docs: **PROIECTE_CONTEXT (live)** and **MODIFICARI_PROIECTE (live)** — read only the DevPilot section. Remove items from MODIFICARI once implemented and confirmed.

## Keep this file current
Update it in the same commit when: build/test/install commands change, a project or folder is added/moved, the integration contract (env vars, `devpilot.tool.json`) changes, or a bug teaches a rule worth remembering. Not for ordinary features. Keep it under ~60 lines.
