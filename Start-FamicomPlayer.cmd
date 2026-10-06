@echo off
if exist "%~dp0release\single-file\FamicomPlayer.exe" (
  start "" "%~dp0release\single-file\FamicomPlayer.exe" %*
  exit /b 0
)
if not exist "%~dp0release\native\FamicomPlayer.exe" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
  if errorlevel 1 exit /b 1
)
start "" "%~dp0release\native\FamicomPlayer.exe" %*
