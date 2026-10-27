# Changelog

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
