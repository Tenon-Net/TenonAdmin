@echo off
rem dev.bat - 一键启动后端 + 前端开发环境
rem 用法：双击，或在仓库根目录运行 dev.bat
rem 后端 http://localhost:5100（MinimalHost）  前端 http://localhost:5173（Vite）
setlocal
cd /d "%~dp0"

echo [api] 启动后端 http://localhost:5100 ...
start "tenon-api" cmd /k "cd /d %~dp0backend && dotnet run --project samples/MinimalHost"

echo [web] 启动前端 http://localhost:5173 ...
start "tenon-web" cmd /k "cd /d %~dp0web && npm install && npm run dev"

echo.
echo 两个服务已在独立窗口中启动。关闭窗口即可停止对应服务。
