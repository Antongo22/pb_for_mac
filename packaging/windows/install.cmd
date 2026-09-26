@echo off
rem Installs PbForMac for the current user. Pass -DesktopShortcut to also create a desktop shortcut.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
pause
