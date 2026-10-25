# Making a tool DevPilot-aware

DevPilot starts every managed tool with these environment variables:

| Variable | Value | Meaning |
|---|---|---|
| `DEVPILOT_MANAGED` | `1` | The tool was started by DevPilot. |
| `DEVPILOT_SUPPRESS` | e.g. `tray,keepawake,autostart` | Features DevPilot owns; the tool should not run its own version. |
| `DEVPILOT_TOOL_ID` | short id | The tool's id in DevPilot (for logs). |
| `DEVPILOT_VERSION` | e.g. `2.0.0` | DevPilot version. |

The list in `DEVPILOT_SUPPRESS` follows the tool's settings in DevPilot:

- `tray` — the tool lives in the DevPilot hub (or gets a DevPilot-drawn icon): **do not create your own tray icon**.
- `keepawake` — **do not** call `SetThreadExecutionState`, move the mouse or press keys to keep the PC awake.
- `autostart` — **do not** write or re-sync your `HKCU\...\Run` entry or Startup-folder shortcut.

Nothing changes when a tool is started any other way, so the tool keeps working on its own.

## Python

```python
import os

_SUPPRESSED = {p.strip().lower() for p in os.environ.get("DEVPILOT_SUPPRESS", "").split(",") if p.strip()}
MANAGED_BY_DEVPILOT = os.environ.get("DEVPILOT_MANAGED") == "1"


def devpilot_owns(feature: str) -> bool:
    """True when DevPilot started this process and owns `feature` ("tray", "keepawake", "autostart")."""
    return MANAGED_BY_DEVPILOT and feature in _SUPPRESSED


# tray
if not devpilot_owns("tray"):
    start_tray_icon()

# keep-awake (e.g. FocusPilot: settings "keep_awake")
if settings.get("keep_awake", True) and not devpilot_owns("keepawake"):
    enable_keep_awake()

# start with Windows (e.g. ScreenPilot re-syncs its Run entry at every start)
if not devpilot_owns("autostart"):
    autostart.sync_from_settings()
```

A tool without a window that hides its tray icon is reachable only through DevPilot, so make sure its useful options are reachable from the command line (see below), or keep `tray` out of the suppression by choosing *Keep the tool's own tray icon* in DevPilot.

## C#

```csharp
static bool DevPilotOwns(string feature) =>
    Environment.GetEnvironmentVariable("DEVPILOT_MANAGED") == "1" &&
    (Environment.GetEnvironmentVariable("DEVPILOT_SUPPRESS") ?? string.Empty)
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Contains(feature, StringComparer.OrdinalIgnoreCase);
```

## Options in the hub

When you hover a tool, DevPilot shows its *actions*. Each action is one of:

| Kind | What it runs |
|---|---|
| `Arguments` | The tool's own program / script / module with these arguments (e.g. `capture window`). |
| `Command` | Any program (`value`) with `arguments`. |
| `Url` | A web page — ideal for tools with a local web UI (regTester). |
| `Folder` / `File` | Opens a folder or a file. |
| `Separator` | A divider. |

`value` and `arguments` may use `{ToolDir}`, `{Home}`, `{Pictures}`, `{Documents}`, `{Desktop}`, `{AppData}`, `{LocalAppData}` and `%VARIABLES%`.

### Python tools in their own virtual environment

A tool installed in a private venv (ScreenPilot's `scripts\install.ps1` uses
`%LOCALAPPDATA%\ScreenPilot\venv`) is not importable by the Python on `PATH`:
`pythonw -m tool` would stop at once with `No module named …`. Set the tool's
**Interpreter** to the venv's `pythonw.exe`; presets list known locations and
DevPilot uses the first that exists when the field is empty. Windowless Python
starts are watched for 2.5 s: if the process ends with an error, its last stderr
line is shown instead of a silent failure.

Windows does not let one program read another program's tray menu, so options that exist only inside a running tool (a Qt/pystray menu) need a command-line entry point. The simplest pattern: a single-instance tool accepts `--action <id>` and forwards it to the running instance (local socket / named pipe), then exits.

## `devpilot.tool.json`

A tool can describe itself. Put `devpilot.tool.json` in the tool's folder (or its parent, for `src` layouts); DevPilot reads it when you pick the tool's file, or with **From devpilot.tool.json** in the editor.

```json
{
  "name": "My Tool",
  "description": "What it does",
  "glyph": "E8A7",
  "icon": "assets/logo.png",
  "pythonModule": "",
  "arguments": "",
  "autoStartArguments": "--minimized",
  "noTrayArguments": "--no-tray",
  "processMatch": "mytool.pyw",
  "healthUrl": "http://127.0.0.1:8765/",
  "actions": [
    { "label": "Open", "glyph": "E774", "kind": "Url", "value": "http://127.0.0.1:8765/" },
    { "label": "Export now", "kind": "Arguments", "arguments": "--action export", "section": "Tasks", "featured": true },
    { "kind": "Separator" },
    { "label": "Data folder", "kind": "Folder", "value": "{LocalAppData}\\MyTool" }
  ]
}
```

`glyph` is a Segoe Fluent / MDL2 code point (`E722`) or any character. All fields are optional.

`icon` is the tool's logo for the hub (and for its separate tray icon): a `.png`, `.ico`, `.jpg`, `.bmp`, or an `.exe` / `.dll` to take the icon from. A relative path starts from the manifest's folder. A square PNG of 64 px or more (or an `.ico` with a 256 px frame) stays sharp at every display scale. The logo shows when the row's *Show in the hub* mode (or the hub-wide *Rows show* setting) includes the logo; otherwise the symbol stays.

### Logos in the hub

Where a row's logo comes from, first match wins:

1. the file chosen under *Tools & groups > Look in the hub > Logo*;
2. the `icon` of the tool's `devpilot.tool.json`;
3. an `icon.ico` or `logo.ico` in the tool's folder (or in its `assets` subfolder), found by name only, no folder scan;
4. the icon of what the entry opens, for programs (`.exe`, `.lnk`) and documents only. Scripts, folders and links would all get the same generic icon, so they need 1, 2 or 3.

The tool editor shows "Logo from: …" under the logo field. The tray icon DevPilot draws for a tool uses the same order, at the size of the display (16 px at 100%, 20 at 125%, 24 at 150%, 32 at 200%): the best frame of a multi-size `.ico`, or a high-quality scaled image or program icon. DevPilot cannot change the icon a tool's own window has in the taskbar; the tool owns that.

Each logo is read once, scaled to a 64 px PNG and cached in `%LOCALAPPDATA%\DevPilot\logo-cache` (safe to delete; it is rebuilt). A changed file is picked up automatically. Loading happens in the background with a short timeout, so a slow or offline drive never delays the hub: the row keeps its symbol.

### Starting a tool when the cloud drive is ready

A tool that lives on OneDrive or Google Drive can fail to start at Windows logon because the drive is not connected yet. In *Tools & groups > Start with DevPilot*, set **Start when** to *Wait until the cloud drive is connected*:

- **Cloud drive**: *Detect from the tool's path* (a `OneDrive…` folder, `My Drive`, `Shared drives`) or pick OneDrive / Google Drive.
- Ready means the drive's app is running (OneDrive, Google Drive), its drive letter is mounted (Google Drive) and the tool's folder is reachable. It is checked every 2 seconds with process and file checks only. Only that tool waits; DevPilot, the tray and other tools start normally. Tools on the same drive share one wait.
- **Timeout** (default 120 s) and **if still not connected**: *Do not start* (default; a notification says so, click it to start the tool, and the tool's log gets a line) or *Start anyway*.
- The delay stays as the minimum wait. Tools on a non-cloud path never wait. The *Startup & conflicts* page shows "Waiting for …" while it lasts.

### Start DevPilot with Windows

*Startup & conflicts > Start DevPilot with Windows* adds `DevPilot --autostart` to `HKCU\...\Run` for your user (no admin rights); *Start hidden* keeps the window closed and leaves only the tray icon. `scripts\install.ps1 -StartWithWindows` does the same at install time. Turn the switch off to remove the entry.

## Current state of your tools

| Tool | Reads `DEVPILOT_SUPPRESS` | Notes |
|---|---|---|
| regTester | via `--no-tray` | All tray options are URLs, so they work 1:1 from the hub. |
| ScreenPilot | yes (0.7.1+) | Options use `action <id>`, forwarded to the running app (it starts when needed); no own tray icon / autostart re-sync when DevPilot owns them; ships `devpilot.tool.json`. Runs from its private venv: DevPilot uses `%LOCALAPPDATA%\ScreenPilot\venv\Scripts\pythonw.exe` when *Interpreter* is empty. |
| FocusPilot | not yet | Keep-awake is on by default and it registers its own autostart; needs the checks above. |
| NexusX | not yet | Needs the tray check; `--minimized` already exists. |
| AutoPilot DC | — | Keeps the PC awake only while a flow runs: no conflict. |

Until a tool reads the variables, its own tray icon stays (Windows 11 can hide it in the overflow area) and DevPilot's *Startup & conflicts* page removes its autostart entry again whenever the tool re-creates it.
