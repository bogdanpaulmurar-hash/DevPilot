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
   - ScreenPilot and NexusX are started as Python modules: choose the project folder (DevPilot uses `src` automatically if it exists). If they use a virtual environment, set **Interpreter** to that venv's `pythonw.exe`.
2. Create your own groups (**Group**) and move tools with the arrows or the *Group* field.
3. Open **Startup & conflicts** → **Take over all**: the tools stop starting with Windows on their own and DevPilot starts them instead.
4. Turn **Keep-awake** on in DevPilot (and nowhere else).
5. Windows 11: right-click the taskbar → *Taskbar settings* → *Other system tray icons* → switch DevPilot **on** so its icon is always visible.

## 4. Moving your configuration from another PC

On the old PC: **Settings → Export…** (or copy `%APPDATA%\DevPilot\config.json`).
On the new PC: **Settings → Import…**. Paths that differ between the two PCs can be fixed in **Tools & groups**.

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
