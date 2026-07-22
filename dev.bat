@echo off
rem dev.bat - one-click start backend + both frontend templates dev env
rem usage: double-click, or run  dev.bat  in repo root
rem backend http://localhost:5100 (MinimalHost)
rem   web (Vue)   http://localhost:5173 (Vite)
rem   web-react   http://localhost:5174 (Vite)  -- 两个模板各占一个端口,可同时对照
setlocal
cd /d "%~dp0"

echo [api] starting backend http://localhost:5100 ...
start "tenon-api" cmd /k "cd /d %~dp0backend && dotnet run --project samples/MinimalHost"

echo [web] starting Vue frontend http://localhost:5173 ...
start "tenon-web" cmd /k "cd /d %~dp0web && npm install && npm run dev"

echo [web-react] starting React frontend http://localhost:5174 ...
start "tenon-web-react" cmd /k "cd /d %~dp0web-react && npm install && npm run dev"

echo.
echo All started in separate windows. Close a window to stop that service.
