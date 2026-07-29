@echo off
setlocal
cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -File "%~dp0Scripts\Test\BuildPlaytestPackage.ps1"
if errorlevel 1 pause
