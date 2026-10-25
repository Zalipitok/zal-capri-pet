@echo off
cd /d "%~dp0"
echo Running Capri's self-test. A second Capri appears briefly, then a results window.
start "" /wait "%~dp0Capri.exe" --selftest
if exist "%~dp0selftest-report.txt" type "%~dp0selftest-report.txt"
pause
