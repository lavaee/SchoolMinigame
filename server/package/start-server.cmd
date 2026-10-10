@echo off
REM Double-click this to start the content server. It prints the address to type into the app once.
title Small Pot content server
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-server.ps1" %*
echo.
echo The server has stopped. This window can be closed.
pause
