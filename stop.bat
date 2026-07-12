@echo off
rem stop.bat - stop dev servers on ports 5100 (api) and 5173 (web)
setlocal enabledelayedexpansion

for %%P in (5100 5173) do (
    set "found="
    for /f "tokens=5" %%I in ('netstat -ano ^| findstr ":%%P " ^| findstr LISTENING') do (
        set "found=1"
        echo [stop] port %%P -> killing pid %%I
        taskkill /PID %%I /T /F >nul 2>&1
    )
    if not defined found echo [stop] port %%P not running
)
echo done.
