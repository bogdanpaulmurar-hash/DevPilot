# Changelog

## Unreleased
- Egress Guard, step 3 of 11 (internal): `Core/Guard/Provenance` reads the git remote (credentials stripped) and commit from the `.git` files without git.exe, and hashes a tool's files (SHA-256 manifest, skipping `.git`, `bin`, `obj`, `node_modules`…, 5000 files max). `GuardApprovals` creates an approval (stored per tool in the config as `GuardApproval`: digest, remote, commit, date, mode, allowlist) and `CheckDrift` reports NotApproved / Unchanged / Changed when files or the remote differ. Tests: new `ProvenanceTests`. Not run locally; CI pending.
- Egress Guard, step 2 of 11 (internal): `Core/Guard/ToolScanner` scans a tool folder (C#, Python, JS/TS, PowerShell, batch, config; skips `bin`, `obj`, `node_modules`, `.git`; 3000 files, 512 KB per file) for HTTP/socket/FTP/SMTP clients, hard-coded URLs and IPs, telemetry, auto-update, uploads, clipboard/screen/key capture, process spawning, persistence, AI clients, embedded secrets and local deny-list terms. Rules are JSON data (`GuardRuleSet`), each with a short why and fix. Findings never contain the matched text. Tests: new `ToolScannerTests`. Not run locally; CI pending.
- Egress Guard, step 1 of 11 (internal, no visible change): Core/Guard model (`EgressMode`, `GuardFinding`, `GuardReport`), `ToolNetworkPolicy` for an optional `network` block in `devpilot.tool.json` (hosts with `*.` wildcard, ports, `allowNone`), and `LocalPolicy` read from `%APPDATA%\DevPilot\policy.local.json` (optional, never logged). Tests: new `GuardModelTests`. Not run locally (no .NET SDK in the authoring environment); CI result pending.

## 2.10.0
- Groups: each group header can have its own logo (.png/.ico/.jpg/.bmp, or the icon of an .exe/.dll/.lnk; it replaces the symbol), a title centred on the row, a larger title (Large / Extra large), a font (hub font, Semibold, Serif, Monospace), bold and italic. Set in Tools & groups > group > Look, with a live preview. Only the header changes; the tools under it keep the hub's look. The count and the chevron stay on the right.
- Backup: group logos are saved in the .zip and restored like tool logos; paths under the old user folder are adapted too.
- Logic in `DevPilot.Core/Hub/GroupHeaderLook.cs`; tests: new `GroupHeaderLookTests`. CI (Windows): build OK, 184/184 tests passed, UI smoke test OK on all pages with 0 binding errors.

## 2.9.0
- Tools & groups > Add > *From a project folder…*: pick a cloned repository and DevPilot works out how to start it. It recognises a `devpilot.tool.json`, a Python package (`python -m`, also under `src`) or script (using the project's `.venv` when present), a C# program (the built `.exe`, else `dotnet run`), a Node app (`npm start`), a launcher script (`start`/`run` `.cmd`/`.bat`/`.ps1`) or a single `.exe`; a known tool (ScreenPilot…) also gets its preset. Anything else is added as a folder. The scan only reads names, 3 levels deep, skips `.git`, `node_modules`, `bin`-like and virtual-environment folders and stops after 1500 entries, so large solutions stay fast. Logic in `DevPilot.Core/Detection/ProjectScanner.cs`.
- Tests: new `ProjectScannerTests`. Not run locally (no .NET SDK in the authoring environment); CI result pending.

## 2.8.0
- Tools & groups: the tool editor explains its fields. Under "What it starts", *How to fill in these fields* lists, for the chosen type (program, Python, PowerShell, batch, command line, folder, link, SSH, WSL…), what each field does with copy-ready examples; *What the other settings mean* covers tokens, hover options, process recognition, health URL, autostart, environment and tray. The text lives in `DevPilot.Core/Help/ToolHelp.cs`.
- Tests: new `ToolHelpTests`. Not run locally (no .NET SDK in the authoring environment); CI result pending.

## Unreleased
- `scripts/install.ps1` now builds from a local copy in `%TEMP%`, like `install.cmd`, so a repository inside Google Drive / OneDrive no longer fails with "Access to apphost.exe is denied".

## 2.7.0
- Virtual Typer: images from the clipboard. The HUD saves the image as a PNG (kept 24 h, max 20 files) and types its path, so a chat that cannot paste images can still reference it. See `docs/TYPER.md`.

## 2.6.0
- Virtual Typer: clipboard history. The last 10 copied texts are kept in memory only (excluded password-manager copies are skipped) and can be picked in the HUD; switch and Clear button in the HUD. See `docs/TYPER.md`.

## 2.5.1
- Virtual Typer: line breaks are typed as Shift+Enter by default (HUD switch `Line break = Shift+Enter`), so multiline text no longer sends a chat message at the first line break.

## 2.5.0
- Virtual Typer: floating HUD that types the clipboard text into the focused window with SendInput (Unicode, multiline), with delay and countdown settings, global Esc stop, stop on window change, confirmation for long texts. Launch from a hub header button or Ctrl+Alt+T (new configs; add the "Virtual Typer" hotkey in Settings on existing ones). See `docs/TYPER.md`.
- Tests: new `TyperTests` (plan, session, options). Not yet run: written without a .NET SDK, CI result pending.
