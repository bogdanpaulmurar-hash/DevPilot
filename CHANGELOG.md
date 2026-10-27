# Changelog

## 2.5.0
- Virtual Typer: floating HUD that types the clipboard text into the focused window with SendInput (Unicode, multiline), with delay and countdown settings, global Esc stop, stop on window change, confirmation for long texts. Launch from a hub header button or Ctrl+Alt+T (new configs; add the "Virtual Typer" hotkey in Settings on existing ones). See `docs/TYPER.md`.
- Tests: new `TyperTests` (plan, session, options). Not yet run: written without a .NET SDK, CI result pending.
