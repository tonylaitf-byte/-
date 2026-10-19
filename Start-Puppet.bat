@echo off
setlocal
cd /d "%~dp0"

where docker >nul 2>nul
if errorlevel 1 (
  echo Docker was not found. Install and start Docker Desktop first.
  pause
  exit /b 1
)

docker info >nul 2>nul
if errorlevel 1 (
  echo Docker Desktop is not running. Start it, wait until it is ready, then run this file again.
  echo If virtualization is unavailable, enable SVM Mode in BIOS and Windows Virtual Machine Platform.
  pause
  exit /b 1
)

docker compose up -d --build
if errorlevel 1 (
  echo Failed to start the hand-tracking container.
  pause
  exit /b 1
)

echo.
echo Hand tracking: http://127.0.0.1:5000/
echo Unity project: unity\UnityPuppetTest
echo.
echo Keep Docker Desktop running while using Unity.
pause
