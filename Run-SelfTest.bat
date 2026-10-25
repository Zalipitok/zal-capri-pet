@echo off
cd /d "%~dp0"
echo Running Capri's self-test. A second Capri appears briefly, then a results window.
powershell.exe -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Capri.ps1" -SelfTest
if exist "%~dp0selftest-report.txt" type "%~dp0selftest-report.txt"
pause
