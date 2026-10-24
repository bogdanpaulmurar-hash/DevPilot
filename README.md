# DevPilot

One tray icon for all your tools. DevPilot is a Windows hub (C# / WPF, .NET 8) that starts, groups and controls your other apps, scripts and folders — so the taskbar stops filling up with one tray icon per tool.

## What it does

- **Tray hub** — click the DevPilot icon: a panel with your tools under the groups *you* define, instant search, and status dots.
- **Options on hover** — hovering a tool shows its options (what used to be in its own tray menu), plus Start / Restart / Stop / Open location.
- **Folders that expand** — reference folders can show their content on hover, with nested sub-folders, filters and limits.
- **Spotlight card** — ScreenPilot gets a special card at the top: big capture buttons, live status and the latest captures as thumbnails.
- **Tray icon per tool, if you want** — each tool can live only in the hub, get a separate icon drawn by DevPilot, or keep its own icon.
- **One owner for duplicated features** — DevPilot is the only one that keeps the PC awake and starts with Windows. Managed tools get `DEVPILOT_MANAGED=1` and `DEVPILOT_SUPPRESS=tray,keepawake,autostart`; the *Startup & conflicts* page takes over their Windows autostart entries (with a backup) and starts them itself.
- **Keep-awake** — silent power request (recommended), mouse nudge or F15 key, only when idle, with a schedule.
- **Monitor** — real process detection (also for Python scripts), health URLs, auto-restart with an hourly limit.
- **Customisable** — theme (dark / light / Windows), one accent colour, hub width / height / corners / opacity / density / text size / position, flyout side and hover delay, group title style; per tool symbol, colour marker, bold, italic.
- **Global shortcuts** — open the hub, start a tool or run one of its options from anywhere.
- **Backups** — one .zip with the whole configuration and the picked logos, manual or automatic (1–10 copies kept), restore with a preview, also on another PC (paths adapted to the new user folder).

## Install

See [docs/INSTALL.md](docs/INSTALL.md). Short version, in PowerShell:

```powershell
git clone https://github.com/bogdanpaulmurar-hash/DevPilot.git
cd DevPilot
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -StartWithWindows
```

If scripts are blocked by policy: `scripts\install.cmd /autostart`.

## Project layout

| Project | Contents |
|---|---|
| `DevPilot.Models` | Configuration model (tools, groups, actions, hub appearance, keep-awake, startup) |
| `DevPilot.Core` | Pure logic: config migration, presets, launch planning, process matching, folder listing, startup matching, schedule, search |
| `DevPilot.Infrastructure` | Windows services: config store, launcher, process monitor, keep-awake, startup manager, hotkeys |
| `DevPilot.UI` | WPF app: tray hub, flyouts, main window pages |
| `DevPilot.Tests` | xUnit tests (run on Windows in GitHub Actions) |

Configuration: `%APPDATA%\DevPilot\config.json` (a v1 file is migrated automatically and kept as `config.v1.bak.json`).
Log: `%LOCALAPPDATA%\DevPilot\logs\devpilot.log`.

## Making a tool DevPilot-aware

See [docs/INTEGRATION.md](docs/INTEGRATION.md): a few lines so a tool hides its own tray icon, keep-awake and autostart when DevPilot manages it, and an optional `devpilot.tool.json` that describes the tool's options.

## Build

```powershell
dotnet build DevPilot.sln -c Release
dotnet test DevPilot.Tests
```

The previous version is kept in the branch `v1-legacy`.
