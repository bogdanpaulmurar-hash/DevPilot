@echo off
rem DevPilot installer without PowerShell scripts (works when script execution is restricted by policy).
rem Usage:  scripts\install.cmd            install / update
rem         scripts\install.cmd /autostart  also start DevPilot with Windows
rem         scripts\install.cmd /uninstall  remove (settings in %%APPDATA%%\DevPilot are kept)
setlocal
set "REPO=%~dp0.."
set "DEST=%LOCALAPPDATA%\Programs\DevPilot"
set "BUILD=%TEMP%\DevPilot-build"
set "RUNKEY=HKCU\Software\Microsoft\Windows\CurrentVersion\Run"
set "LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\DevPilot.lnk"

taskkill /im DevPilot.exe /f >nul 2>&1

if /i "%~1"=="/uninstall" (
    reg delete "%RUNKEY%" /v DevPilot /f >nul 2>&1
    del "%LNK%" >nul 2>&1
    rmdir /s /q "%DEST%" >nul 2>&1
    echo DevPilot removed. Settings are still in %APPDATA%\DevPilot.
    exit /b 0
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET SDK not found. Install the .NET 8 SDK ^(x64^) and run this again.
    exit /b 1
)

rem Build from a local copy: the repository may live in a synced folder ^(Google Drive, OneDrive^).
echo Copying sources to %BUILD% ...
robocopy "%REPO%" "%BUILD%" /MIR /XD .git bin obj .vs publish TestResults /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
    echo Copy failed.
    exit /b 1
)

echo Building DevPilot ...
dotnet publish "%BUILD%\DevPilot.UI\DevPilot.UI.csproj" -c Release -r win-x64 --self-contained false -o "%DEST%" --nologo
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut($env:APPDATA+'\Microsoft\Windows\Start Menu\Programs\DevPilot.lnk'); $s.TargetPath=$env:LOCALAPPDATA+'\Programs\DevPilot\DevPilot.exe'; $s.WorkingDirectory=$env:LOCALAPPDATA+'\Programs\DevPilot'; $s.Save()" >nul 2>&1

if /i "%~1"=="/autostart" (
    reg add "%RUNKEY%" /v DevPilot /t REG_SZ /d "\"%DEST%\DevPilot.exe\" --autostart" /f >nul
    echo DevPilot will start with Windows.
)

echo Installed to %DEST%
start "" "%DEST%\DevPilot.exe"
endlocal
