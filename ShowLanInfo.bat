@echo off
setlocal
cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -File "%~dp0Scripts\Test\ShowLanPlaytestInfo.ps1"
pause
