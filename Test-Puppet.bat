@echo off
setlocal
cd /d "%~dp0"

docker compose ps
powershell -NoProfile -ExecutionPolicy Bypass -Command "$h=Invoke-RestMethod http://127.0.0.1:5000/health; $s=Invoke-RestMethod http://127.0.0.1:5000/puppet_state; $h; $s"
pause
