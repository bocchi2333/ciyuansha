@echo off
setlocal
cd /d "%~dp0"
set PORT=24567
set /p HOST_ADDRESS=Host LAN IP or IP:port: 
if "%HOST_ADDRESS%"=="" (
  echo Host address is required.
  pause
  exit /b 1
)
set /p PLAYER_NAME=Client player name [Client]: 
if "%PLAYER_NAME%"=="" set PLAYER_NAME=Client
powershell -ExecutionPolicy Bypass -File "%~dp0Scripts\Test\LaunchLanPlaytest.ps1" -Role client -Address "%HOST_ADDRESS%" -PlayerName "%PLAYER_NAME%" -Port %PORT% -AutoReady
if errorlevel 1 pause
