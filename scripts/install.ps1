<#
.SYNOPSIS
    Builds DevPilot from this folder and installs it for the current user.
.PARAMETER StartWithWindows
    Register DevPilot to start with Windows (HKCU, no admin rights).
.PARAMETER Uninstall
    Remove the installed files, the shortcut and the autostart entry (settings are kept).
#>
param(
    [switch]$StartWithWindows,
    [switch]$Uninstall,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\DevPilot')
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $InstallDir 'DevPilot.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'DevPilot.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

function Stop-DevPilot {
    Get-Process -Name DevPilot -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "Stopping DevPilot (pid $($_.Id))..."
        $_.CloseMainWindow() | Out-Null
        Start-Sleep -Milliseconds 800
        if (-not $_.HasExited) { $_.Kill() }
    }
}

if ($Uninstall) {
    Stop-DevPilot
    Remove-ItemProperty -Path $runKey -Name 'DevPilot' -ErrorAction SilentlyContinue
    Remove-Item $shortcut -ErrorAction SilentlyContinue
    if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
    Write-Host 'DevPilot removed. Settings are still in %APPDATA%\DevPilot.'
    return
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK not found. Install it with: winget install --id Microsoft.DotNet.SDK.8 -e'
}
$sdks = (& dotnet --list-sdks) -join "`n"
if ($sdks -notmatch '^(8|9|10)\.') {
    throw ".NET 8 SDK (or newer) is required. Found:`n$sdks"
}

Stop-DevPilot

# Build from a local copy: the repository may live in a synced folder (Google Drive, OneDrive) that locks obj\ files.
$build = Join-Path $env:TEMP 'DevPilot-build'
Write-Host "Copying sources to $build ..."
& robocopy $repo $build /MIR /XD .git bin obj .vs publish TestResults /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Copy failed.' }

Write-Host "Building DevPilot..."
& dotnet publish (Join-Path $build 'DevPilot.UI\DevPilot.UI.csproj') -c Release -r win-x64 --self-contained false -o $InstallDir --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($shortcut)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $InstallDir
$lnk.IconLocation = "$exe,0"
$lnk.Description = 'DevPilot — one tray hub for all your tools'
$lnk.Save()
Write-Host "Start-menu shortcut: $shortcut"

if ($StartWithWindows) {
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name 'DevPilot' -Value "`"$exe`" --autostart"
    Write-Host 'DevPilot will start with Windows.'
}

Write-Host "Installed to $InstallDir"
Start-Process $exe
