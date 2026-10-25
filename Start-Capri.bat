@echo off
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Capri.ps1"
if errorlevel 1 (
 echo.
 echo Capri could not start. Copy the error above for troubleshooting.
 pause
)
