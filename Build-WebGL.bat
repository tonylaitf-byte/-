@echo off
setlocal

set "UNITY_EXE=C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe"
set "PROJECT_DIR=%~dp0unity\UnityPuppetTest"
set "LOG_FILE=%~dp0unity-webgl-build.log"

if not exist "%UNITY_EXE%" (
  echo Unity 6000.5.9f1 was not found.
  exit /b 1
)

"%UNITY_EXE%" -batchmode -quit -projectPath "%PROJECT_DIR%" -executeMethod PuppetWebGLBuilder.BuildFromCommandLine -logFile "%LOG_FILE%"
if errorlevel 1 (
  echo Unity WebGL build failed. Check "%LOG_FILE%".
  exit /b 1
)

echo Unity WebGL build completed.
echo Open http://127.0.0.1:5000/control.html after starting the Flask service.
exit /b 0
