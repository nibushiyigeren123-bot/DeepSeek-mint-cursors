@echo off
setlocal
set "PSModulePath="
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0apply-updated.ps1" -Character deepseek -Size 32
set "TaskExit=%errorlevel%"
if not "%TaskExit%"=="0" pause
exit /b %TaskExit%
