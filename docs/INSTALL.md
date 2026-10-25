# Installing DevPilot on a new laptop

DevPilot runs on Windows 10/11 and needs .NET 8. It is built on your machine from the source, so no downloaded `.exe` is involved.

## 1. Prerequisites (once)

Open **PowerShell** (normal user, not administrator) and check:

```powershell
git --version
dotnet --list-sdks
```

If one is missing:

```powershell
winget install --id Git.Git -e
winget install --id Microsoft.DotNet.SDK.8 -e
```

Close and reopen PowerShell after installing.

## 2. Get the code and install

```powershell
cd $HOME\source          # or any folder you like
git clone https://github.com/bogdanpaulmurar-hash/DevPilot.git
cd DevPilot
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -StartWithWindows
```

The script:

1. stops a running DevPilot (if any),
2. builds and publishes it to `%LOCALAPPDATA%\Programs\DevPilot`,
3. creates a Start-menu shortcut,
4. with `-StartWithWindows`, registers DevPilot to start with Windows (for your user only),
5. starts DevPilot.

Your settings are not touched (`%APPDATA%\DevPilot\config.json`).

### If PowerShell scripts are blocked (company laptops)

When `install.ps1` fails with *"is not digitally signed"*, script execution is enforced by policy and `-ExecutionPolicy Bypass` is ignored. Use the batch installer instead — same steps, no PowerShell script:

```bat
scripts\install.cmd /autostart
```

It also builds from a local copy, so the repository can stay in Google Drive / OneDrive.

If `winget` reports *0x8a15000f* (source data missing), install the **.NET 8 SDK x64** from https://dotnet.microsoft.com/download/dotnet/8.0 (or from your company software portal) and check with `dotnet --list-sdks`.

## 3. First start

1. DevPilot opens on **Tools & groups**. Click **Add → Your known tools** and pick ScreenPilot, FocusPilot, NexusX, regTester or AutoPilot DC; choose the script / exe / project folder when asked.
   - ScreenPilot and NexusX are started as Python modules: choose the project folder (DevPilot uses `src` automatically if it exists). If they use a virtual environment, set **Interpreter** to that venv's `pythonw.exe`. ScreenPilot installed with its `install.ps1` is found automatically (`%LOCALAPPDATA%\ScreenPilot\venv`).
2. Create your own groups (**Group**) and move tools with the arrows or the *Group* field.
3. Open **Startup & conflicts** → **Take over all**: the tools stop starting with Windows on their own and DevPilot starts them instead.
4. Turn **Keep-awake** on in DevPilot (and nowhere else).
5. Start with Windows: **Startup & conflicts** → *Start DevPilot with Windows* (or install with `-StartWithWindows`). *Start hidden* opens only the tray icon.
6. Optional: **Settings** → *DevPilot icon* to use your own icon for the tray icon and window.
7. Windows 11: right-click the taskbar → *Taskbar settings* → *Other system tray icons* → switch DevPilot **on** so its icon is always visible.

## 4. Backups and moving to another PC

**Settings → Backup** writes one `.zip` (`DevPilot-backup-YYYYMMDD-HHMMSS.zip`) with `config.json` (tools, groups,
colours, symbols, shortcuts, hub look), the logo files you picked and a `manifest.json`.

- **Folder**: default `Documents\DevPilot Backups`; any local, network or synced (Drive / OneDrive) folder works.
- **Backups**: manual only, once a day when DevPilot starts, or every start. **Copies kept**: 1–10; older
  DevPilot backups are removed, other files in the folder are never touched.
- **Back up now**: in Settings, in the tray quick menu, or as a global shortcut action.
- **Restore…**: pick a backup, check the summary (what comes back, what goes away, paths not found on this PC),
  confirm. A `…-before-restore.zip` of the current configuration is made first (the newest 3 are kept).
  Logos are copied to `%APPDATA%\DevPilot\logos`. Start with Windows, the autostart entries DevPilot took over,
  the window size and the backup settings stay as they are on this PC.
- **Another user name**: with "adapt paths from another user folder" on (default), `C:\Users\old-name\…`
  becomes this PC's user folder. Paths that still do not exist are kept and listed, so you can fix them in
  **Tools & groups**.

**Export… / Import…** (single `config.json`, without logos) still work as before.

## 5. Updating

```powershell
cd $HOME\source\DevPilot
git pull
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

## 6. Uninstalling

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -Uninstall
```

Before uninstalling, restore taken-over autostart entries in **Startup & conflicts** if you want the tools to start on their own again.
