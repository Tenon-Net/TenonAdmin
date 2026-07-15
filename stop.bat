@echo off
rem stop.bat - 停止端口 5100（api）和 5173（web）上的开发服务
setlocal enabledelayedexpansion

for %%P in (5100 5173) do (
    set "found="
    for /f "tokens=5" %%I in ('netstat -ano ^| findstr ":%%P " ^| findstr LISTENING') do (
        set "found=1"
        echo [stop] 端口 %%P -^> 终止进程 %%I
        taskkill /PID %%I /T /F >nul 2>&1
    )
    if not defined found echo [stop] 端口 %%P 未运行
)
echo 完成。
