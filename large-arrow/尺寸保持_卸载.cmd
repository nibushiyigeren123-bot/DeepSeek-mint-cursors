@echo off
setlocal
set "PSModulePath="
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0keeper.ps1" -Action Uninstall
set "TaskExit=%errorlevel%"
if not "%TaskExit%"=="0" pause
exit /b %TaskExit%
