@echo off
setlocal

cd /d "%~dp0"

set "SCRIPT_NAME=%~nx0"
set "NO_PAUSE=0"
if /i "%~1"=="--no-pause" (
    set "NO_PAUSE=1"
    shift
)
if not "%~1"=="" (
    echo Usage: %SCRIPT_NAME% [--no-pause]
    exit /b 64
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "if (@(Get-NetUDPEndpoint -LocalPort 5005 -ErrorAction SilentlyContinue).Count -gt 0) { exit 1 } else { exit 0 }"
if errorlevel 1 (
    echo UDP port 5005 is already in use.
    echo MotionBricks server was not started, so the model was not loaded.
    echo Use StopMotionBricksServer.cmd only if this is the MotionBricks server.
    call :pause_if_interactive
    exit /b 1
)

if not exist "Bridge\.venv\Scripts\python.exe" (
    echo Missing Bridge\.venv\Scripts\python.exe.
    echo Run Bridge\setup_windows.ps1 first.
    call :pause_if_interactive
    exit /b 1
)
if not exist "Bridge\motionbricks_server.py" (
    echo Missing Bridge\motionbricks_server.py.
    call :pause_if_interactive
    exit /b 1
)

set "TORCH_FORCE_NO_WEIGHTS_ONLY_LOAD=1"
echo Starting MotionBricks server on UDP port 5005...
"Bridge\.venv\Scripts\python.exe" -u "Bridge\motionbricks_server.py"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" echo MotionBricks server exited with code %EXIT_CODE%.
call :pause_if_interactive
exit /b %EXIT_CODE%

:pause_if_interactive
if "%NO_PAUSE%"=="0" pause
exit /b
