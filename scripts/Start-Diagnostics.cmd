@echo off
setlocal
set "COREHOST_TRACE=1"
set "COREHOST_TRACEFILE=%TEMP%\SRBMiner-Monitor-host.log"
echo Starting SRBMiner Monitor with diagnostics...
start "" /wait "%~dp0SRBMiner Monitor.exe"
echo.
echo Exit code: %ERRORLEVEL%
echo Startup logs: %LOCALAPPDATA%\SRBMiner Monitor\Logs
echo Runtime host log: %COREHOST_TRACEFILE%
echo.
if exist "%LOCALAPPDATA%\SRBMiner Monitor\Logs" explorer "%LOCALAPPDATA%\SRBMiner Monitor\Logs"
pause
