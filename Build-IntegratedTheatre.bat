@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-IntegratedTheatre.ps1" %*
exit /b %errorlevel%
