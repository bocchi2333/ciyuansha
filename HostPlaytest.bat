@echo off
setlocal
cd /d "%~dp0"
set PORT=24567
set /p PLAYER_NAME=Host player name [Host]: 
if "%PLAYER_NAME%"=="" set PLAYER_NAME=Host
powershell -ExecutionPolicy Bypass -File "%~dp0Scripts\Test\LaunchLanPlaytest.ps1" -Role host -PlayerName "%PLAYER_NAME%" -Port %PORT% -AutoReady -AutoStart
if errorlevel 1 pause
