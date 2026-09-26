# Installs PbForMac for the current user (no administrator rights needed):
#   app       -> %LOCALAPPDATA%\Programs\PbForMac
#   shortcuts -> Start menu (and Desktop with -DesktopShortcut)
#   .pbm report files open in PbForMac
# Usage:     powershell -ExecutionPolicy Bypass -File install.ps1 [-DesktopShortcut]
# Uninstall: Settings > Apps > PbForMac, uninstall.cmd, or install.ps1 -Uninstall
param(
    [switch]$Uninstall,
    [switch]$DesktopShortcut
)

$ErrorActionPreference = 'Stop'

$target = Join-Path $env:LOCALAPPDATA 'Programs\PbForMac'
$exe = Join-Path $target 'PbForMac.exe'
$startMenuLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'PbForMac.lnk'
$desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PbForMac.lnk'
$classes = 'HKCU:\Software\Classes'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PbForMac'

if ($Uninstall) {
    Get-Process PbForMac -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $startMenuLink, $desktopLink -Force -ErrorAction SilentlyContinue
    Remove-Item "$classes\.pbm", "$classes\PbForMac.Report", $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'PbForMac has been removed.'
    return
}

$source = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path (Join-Path $source 'PbForMac.exe'))) {
    throw "PbForMac.exe not found in $source. Run the script from the unpacked archive."
}

Get-Process PbForMac -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
# Files downloaded from the internet are marked as blocked; unblock them.
Get-ChildItem $target -Recurse -File | Unblock-File

function New-Shortcut([string]$path) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $target
    $link.IconLocation = "$exe,0"
    $link.Description = 'PbForMac - data analysis and dashboards'
    $link.Save()
}

New-Shortcut $startMenuLink
if ($DesktopShortcut) { New-Shortcut $desktopLink }

# Associate .pbm reports with the app (current user only).
New-Item -Path "$classes\.pbm" -Force | Out-Null
Set-Item -Path "$classes\.pbm" -Value 'PbForMac.Report'
New-Item -Path "$classes\PbForMac.Report\DefaultIcon" -Force | Out-Null
Set-Item -Path "$classes\PbForMac.Report" -Value 'PbForMac report'
Set-Item -Path "$classes\PbForMac.Report\DefaultIcon" -Value "`"$exe`",0"
New-Item -Path "$classes\PbForMac.Report\shell\open\command" -Force | Out-Null
Set-Item -Path "$classes\PbForMac.Report\shell\open\command" -Value "`"$exe`" `"%1`""

# Register in Settings > Apps so the app can be removed from there.
$uninstaller = Join-Path $target 'uninstall.ps1'
Copy-Item -Path $PSCommandPath -Destination $uninstaller -Force
New-Item -Path $uninstallKey -Force | Out-Null
$version = ((Get-Item $exe).VersionInfo.ProductVersion -split "\+")[0]
Set-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'PbForMac'
Set-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value $version
Set-ItemProperty -Path $uninstallKey -Name Publisher -Value 'Anton Aleynichenko'
Set-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value "`"$exe`",0"
Set-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $target
Set-ItemProperty -Path $uninstallKey -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`" -Uninstall"
Set-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -Type DWord

Write-Host "PbForMac installed to $target"
Write-Host 'Launch it from the Start menu.'
