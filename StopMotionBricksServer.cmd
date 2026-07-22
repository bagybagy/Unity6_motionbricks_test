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

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$endpoints = @(Get-NetUDPEndpoint -LocalPort 5005 -ErrorAction SilentlyContinue); if ($endpoints.Count -eq 0) { Write-Host 'No process is listening on UDP port 5005.'; exit 0 }; $processIds = @(); foreach ($endpoint in $endpoints) { $ownerId = [int]$endpoint.OwningProcess; if ($processIds -notcontains $ownerId) { $processIds += $ownerId } }; $unsafe = $false; foreach ($processId in $processIds) { $process = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $processId) -ErrorAction SilentlyContinue; $isPython = $null -ne $process -and $process.Name -match '^python(?:\.exe)?$'; $isMotionBricks = $null -ne $process -and $process.CommandLine -match '(?i)motionbricks_server\.py(?:\s|$|\")'; if (-not ($isPython -and $isMotionBricks)) { $name = if ($null -ne $process) { $process.Name } else { 'not found' }; Write-Host ('Refusing to stop PID ' + $processId + ': expected python running motionbricks_server.py, found ' + $name + '.'); $unsafe = $true } }; if ($unsafe) { exit 2 }; foreach ($processId in $processIds) { try { Stop-Process -Id $processId -Force -ErrorAction Stop; Write-Host ('Stopped MotionBricks server PID ' + $processId + '.') } catch { Write-Host ('Failed to stop MotionBricks server PID ' + $processId + ': ' + $_.Exception.Message); exit 1 } }"
set "EXIT_CODE=%ERRORLEVEL%"
call :pause_if_interactive
exit /b %EXIT_CODE%

:pause_if_interactive
if "%NO_PAUSE%"=="0" pause
exit /b
