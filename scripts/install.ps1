# Builds PbForMac from source and installs it for the current user (Windows).
#   app       -> %LOCALAPPDATA%\Programs\PbForMac
#   shortcuts -> Start menu (and Desktop with -DesktopShortcut), .pbm association, Settings > Apps entry
# Usage:     powershell -ExecutionPolicy Bypass -File scripts\install.ps1 [-DesktopShortcut]
# Uninstall: powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Uninstall
# Requires the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
param(
    [switch]$Uninstall,
    [switch]$DesktopShortcut
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$installer = Join-Path $root 'packaging\windows\install.ps1'

if ($Uninstall) {
    & $installer -Uninstall
    return
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 10 SDK is required: https://dotnet.microsoft.com/download/dotnet/10.0'
}

$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
$rid = if ($arch -eq [System.Runtime.InteropServices.Architecture]::Arm64) { 'win-arm64' } else { 'win-x64' }
$stage = Join-Path $root "artifacts\PbForMac-$rid"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
dotnet publish (Join-Path $root 'src\PbForMac\PbForMac.csproj') -c Release -r $rid --self-contained true -o (Join-Path $stage 'app')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Copy-Item $installer $stage
& (Join-Path $stage 'install.ps1') -DesktopShortcut:$DesktopShortcut
