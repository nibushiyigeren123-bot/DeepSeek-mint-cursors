@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0apply-updated.ps1" -Character deepseek -Size 32
if errorlevel 1 (
 echo Installation failed. Please read the error above.
 pause
 exit /b 1
)
echo All 17 cursors applied. Enable the optional size keeper once if needed.
pause
