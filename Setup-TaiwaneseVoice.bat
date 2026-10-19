@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0voice-tools\setup_taiwanese_voice.ps1" %*
exit /b %ERRORLEVEL%
