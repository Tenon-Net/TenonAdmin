@echo off
rem dev.bat - one-click start backend + frontend dev env
rem usage: double-click, or run  dev.bat  in repo root
rem backend http://localhost:5000 (MinimalHost)  frontend http://localhost:5173 (Vite)
setlocal
cd /d "%~dp0"

if not exist "web\node_modules" (
    echo [web] installing deps: npm install ...
    pushd web && call npm install && popd
)

echo [api] starting backend http://localhost:5000 ...
start "tenon-api" cmd /k "cd /d %~dp0backend && dotnet run --project samples/MinimalHost"

echo [web] starting frontend http://localhost:5173 ...
start "tenon-web" cmd /k "cd /d %~dp0web && npm run dev"

echo.
echo Both started in separate windows. Close a window to stop that service.
