@echo off
setlocal
set "PSModulePath="
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Restore
set "TaskExit=%errorlevel%"
if not "%TaskExit%"=="0" pause
exit /b %TaskExit%
