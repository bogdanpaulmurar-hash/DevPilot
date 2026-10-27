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

### Field reference

The manifest only describes the tool. *What* DevPilot starts (an `.exe`, a script, a module) comes from the file you pick when you add the tool; the manifest fills in the rest. Field names are case-insensitive, unknown fields are ignored, and `//` comments and trailing commas are allowed.

| Field | What it does | Possible values | If it is missing |
|---|---|---|---|
| `name` | Name shown in the hub and in the tray menu. | Any text. | The name you typed (or the file name) stays. |
| `description` | Tooltip / second line in the hub. | Any text. | No description. |
| `glyph` | Symbol used when no logo is shown. | Segoe Fluent / MDL2 code point (`E722`) or any character. | The tool keeps its current symbol. |
| `icon` | Logo for the hub and the tool's own tray icon. | Path to `.png`, `.ico`, `.jpg`, `.bmp`, `.exe`, `.dll`; relative to the manifest's folder, or absolute. | Logo rules below (step 3, or the symbol). |
| `pythonModule` | Python tools only: start with `pythonw -m <module>` instead of running the picked script. | Module name, e.g. `screenpilot`. | The picked `.py` / `.pyw` file is run. |
| `arguments` | Arguments for every normal start. | Command-line text; `{tokens}` and `%VARIABLES%` are expanded. | No arguments. |
| `autoStartArguments` | Arguments used **instead of** `arguments` when DevPilot starts the tool with Windows. | e.g. `--minimized`. | `arguments` is used for autostart too. |
| `noTrayArguments` | Added to the start command when DevPilot owns the tray (the tool sits in the hub). For tools that do not read `DEVPILOT_SUPPRESS`. | e.g. `--no-tray`. | Nothing is added; the tool should check `DEVPILOT_SUPPRESS` itself. |
| `processMatch` | How DevPilot recognises the running tool (status dot, *Stop*, *Restart*). Several patterns separated by `;`. | `name.exe` = process name; a bare word = process name; anything else = text in the command line (case-insensitive). For Python tools only `python`/`pythonw`/`py` processes are checked, unless the pattern ends in `.exe`. | Derived from the target: the `.exe` name, the script name, or `-m <module>`. |
| `healthUrl` | Address DevPilot checks to see whether the tool answers. It decides the status when the tool cannot be tracked as a process (e.g. a link or a service started elsewhere). | `http://127.0.0.1:<port>/…` | No web check. |
| `actions` | The tool's options in the hub (its old tray menu). | List of actions, see below. | The tool keeps the actions it already has in DevPilot. |

When the manifest is applied, `actions` **replace** the tool's current actions; every other field only fills a field that is still empty (the editor's **From devpilot.tool.json** can also overwrite them).

Each entry in `actions`:

| Field | What it does | Possible values | If it is missing |
|---|---|---|---|
| `label` | Text of the option. | Any text. | Empty label. |
| `kind` | What the option runs (table under *Options in the hub*). | `Arguments`, `Command`, `Url`, `Folder`, `File`, `Separator`. | `Arguments`. |
| `value` | Program (`Command`), address (`Url`), folder or file. Not used by `Arguments`. | Path or URL; `{tokens}` and `%VARIABLES%` are expanded. | Empty: `Url` / `Folder` / `File` / `Command` have nothing to open. |
| `arguments` | Arguments for `Arguments` and `Command` (and for `File`, passed to the opening program). | Command-line text. | No arguments. |
| `glyph` | Symbol next to the option. | Like the tool's `glyph`. | No symbol. |
| `section` | Groups options with the same text under one heading in the flyout. | Any text, e.g. `Tasks`. | No heading. |
| `featured` | Shows the option as a large button on the spotlight card. | `true` / `false`. | `false`. |
| `bold` | Shows the option in bold. | `true` / `false`. | `false`. |
| `id` | Stable id of the option. | Short text. | Generated automatically. |

Tokens: `{ToolDir}`, `{Path}`, `{Name}`, `{Home}`, `{Pictures}`, `{Documents}`, `{Desktop}`, `{AppData}`, `{LocalAppData}`.

### Ready-to-copy examples

Save the file as `devpilot.tool.json` next to the program (or one folder up), then add the program in DevPilot.

**1. Python app with its own tray icon** (pick `mytool.pyw`). The tool hides its tray icon when DevPilot owns it; its menu options are forwarded to the running instance with `--action`.

```json
{
  "name": "My Tool",
  "description": "Python tray app",
  "glyph": "E943",
  "icon": "assets/logo.png",
  "autoStartArguments": "--minimized",
  "processMatch": "mytool.pyw",
  "actions": [
    { "label": "Open window", "kind": "Arguments", "arguments": "--action show", "featured": true },
    { "label": "Run now", "kind": "Arguments", "arguments": "--action run", "section": "Tasks" },
    { "kind": "Separator" },
    { "label": "Logs", "kind": "Folder", "value": "{LocalAppData}\\MyTool\\logs" }
  ]
}
```

**2. Web app on a local port** (pick `server.py`). DevPilot starts the server without its tray icon (`--no-tray`) and also checks the page address.

```json
{
  "name": "My Web Tool",
  "description": "Local web app on port 8765",
  "glyph": "E774",
  "icon": "web/icon.png",
  "noTrayArguments": "--no-tray",
  "processMatch": "server.py",
  "healthUrl": "http://127.0.0.1:8765/",
  "actions": [
    { "label": "Open", "kind": "Url", "value": "http://127.0.0.1:8765/", "featured": true, "bold": true },
    { "label": "Reports", "kind": "Url", "value": "http://127.0.0.1:8765/#reports" },
    { "kind": "Separator" },
    { "label": "Data folder", "kind": "Folder", "value": "{LocalAppData}\\MyWebTool" }
  ]
}
```

**3. C# program (.exe)** (pick `MyApp.exe`). The logo is taken from the `.exe` itself.

```json
{
  "name": "My App",
  "description": "C# desktop app",
  "icon": "MyApp.exe",
  "autoStartArguments": "--minimized",
  "processMatch": "MyApp.exe",
  "actions": [
    { "label": "Settings", "kind": "Arguments", "arguments": "--settings" },
    { "label": "Config file", "kind": "File", "value": "{AppData}\\MyApp\\config.json" },
    { "label": "Install folder", "kind": "Folder", "value": "{ToolDir}" }
  ]
}
```

**4. Command-line script** (pick `run.bat`, `run.ps1` or `cli.py`). No window or tray of its own: every option is one run of the script with different arguments.

```json
{
  "name": "My Script",
  "description": "Command-line jobs",
  "glyph": "E756",
  "actions": [
    { "label": "Sync", "kind": "Arguments", "arguments": "sync", "section": "Jobs", "featured": true },
    { "label": "Clean up", "kind": "Arguments", "arguments": "cleanup --days 30", "section": "Jobs" },
    { "label": "Open output", "kind": "Folder", "value": "{ToolDir}\\output" },
    { "label": "Edit config", "kind": "Command", "value": "notepad.exe", "arguments": "\"{ToolDir}\\config.ini\"" }
  ]
}
```

### Logos in the hub

Where a row's logo comes from, first match wins:

1. the file chosen under *Tools & groups > Look in the hub > Logo*;
2. the `icon` of the tool's `devpilot.tool.json`;
3. the icon of what the entry opens, for programs (`.exe`, `.lnk`) and documents only. Scripts, folders and links would all get the same generic icon, so they need 1 or 2.

Each logo is read once, scaled to a 64 px PNG and cached in `%LOCALAPPDATA%\DevPilot\logo-cache` (safe to delete; it is rebuilt). A changed file is picked up automatically. Loading happens in the background with a short timeout, so a slow or offline drive never delays the hub: the row keeps its symbol.

## Current state of your tools

| Tool | Reads `DEVPILOT_SUPPRESS` | Notes |
|---|---|---|
| regTester | via `--no-tray` | All tray options are URLs, so they work 1:1 from the hub. |
| ScreenPilot | yes (0.7.1+) | Options use `action <id>`, forwarded to the running app (it starts when needed); no own tray icon / autostart re-sync when DevPilot owns them; ships `devpilot.tool.json`. Runs from its private venv: DevPilot uses `%LOCALAPPDATA%\ScreenPilot\venv\Scripts\pythonw.exe` when *Interpreter* is empty. |
| FocusPilot | not yet | Keep-awake is on by default and it registers its own autostart; needs the checks above. |
| NexusX | not yet | Needs the tray check; `--minimized` already exists. |
| AutoPilot DC | — | Keeps the PC awake only while a flow runs: no conflict. |

Until a tool reads the variables, its own tray icon stays (Windows 11 can hide it in the overflow area) and DevPilot's *Startup & conflicts* page removes its autostart entry again whenever the tool re-creates it.
