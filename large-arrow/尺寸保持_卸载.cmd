@echo off
setlocal
set "PSModulePath="
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0keeper.ps1" -Action Uninstall
if errorlevel 1 pause
