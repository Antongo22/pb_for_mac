@echo off
rem Removes PbForMac installed by install.cmd.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall
pause
