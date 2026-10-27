# Virtual Typer

Types the text from the clipboard into the focused window, key by key, as if typed by hand. Use it where paste (Ctrl+V)
is blocked, for example inside some embedded web views.

## Use
1. Copy the text.
2. Open the Virtual Typer: keyboard icon in the hub header, or the hotkey **Ctrl+Alt+T** (Settings > Hotkeys, action "Virtual Typer").
3. Check the preview, set the delay and the countdown, press **Start typing**.
4. Click in the target field during the countdown. Typing starts when it reaches 0.

The window stays on top, can be dragged by its title and remembers its position. It never takes the keyboard focus.

Line breaks are typed as **Shift+Enter** by default (switch in the HUD), because in chat inputs such as the Claude extension a plain Enter sends the message. Turn it off for editors and terminals, where Enter is the line break.

## Clipboard history
DevPilot keeps the last 10 copied texts in memory (never on disk; forgotten on exit). Open the HUD and pick one from the list; the newest is selected. Long texts are shown as one short line, the preview shows the first 300 characters, typing uses the whole text.
- Texts that password managers mark as excluded from clipboard history are not stored.
- **Clear** forgets the list; the switch "Keep clipboard history" turns collecting off (and clears it).
- It listens to the Windows clipboard-change event, no polling.

## Images
If the clipboard holds an image (and no text), the HUD saves it as a PNG in `%LOCALAPPDATA%\DevPilot\clipboard-images` and offers the file path as the text to type. Type the path into a chat that supports file references (for example `@` + path in the Claude Code extension) to let the chat read the image.
- The image is saved only when the HUD opens or is reloaded, never in the background, and not twice for the same image.
- Files are deleted automatically after 24 hours, and only the newest 20 are kept.
- Images are not part of the clipboard history list.

## Safety
- **Esc** (global while a run is active) or the **Stop** button stops before the next key.
- Typing stops if another window becomes active.
- Texts over 2000 characters need a second click on Start to confirm.
- Hard limit of 1,000,000 characters per run.
- An elevated (administrator) window in front rejects the input; the HUD reports it.

## Settings (`typer` in `config.json`)
`DelayMs` (0-500, the HUD slider covers 0-200), `CountdownSeconds` (0-30), `StopOnWindowChange`, `LineBreakAsShiftEnter`, `KeepClipboardHistory`, `ConfirmAboveChars`, `HudLeft`, `HudTop`.
Delays under about 15 ms are rounded up by Windows timers; raise the delay if letters are lost.

## Code
`DevPilot.Core/Typer` (plan and session, tested), `DevPilot.Infrastructure/Services/KeyboardSender.cs` (SendInput),
`DevPilot.UI/Typer` (HUD and controller). Images from the clipboard are not handled yet.
