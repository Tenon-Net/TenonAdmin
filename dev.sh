#!/usr/bin/env bash
# dev.sh - 一条命令启动后端 + 两个前端模板开发环境（macOS / Linux，对应 Windows 的 dev.bat）
# 后端 http://localhost:5100（MinimalHost）
#   web (Vue)  http://localhost:5173（Vite）
#   web-react  http://localhost:5174（Vite）—— 两个模板各占一个端口,可同时对照
# 后端使用 5100 而非 5000：macOS 的 AirPlay 接收器默认占用 5000。
# 用法：在仓库根目录运行 ./dev.sh    停止运行 ./stop.sh
set -euo pipefail
cd "$(dirname "$0")"

API_PORT=5100
# Vite 代理读取此变量以覆盖默认目标，确保前端代理到同一个后端端口（唯一真实来源 = 此文件）。
export TENON_API_TARGET="http://localhost:$API_PORT"

if lsof -iTCP:"$API_PORT" -sTCP:LISTEN -n -P >/dev/null 2>&1; then
  echo "[!] 端口 $API_PORT 已被占用——后端可能已在运行。请先执行 ./stop.sh。"
  exit 1
fi

mkdir -p .dev

echo "[api] 启动后端 http://localhost:$API_PORT ..."
# --no-launch-profile：跳过 Properties/launchSettings.json（避免其 applicationUrl 覆盖下面的 URL）。
# 该配置文件同时设置的 Development 环境需要手动恢复（OpenAPI 仅在开发环境可用）。
( cd backend && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:$API_PORT" \
    dotnet run --no-launch-profile --project samples/MinimalHost ) > .dev/api.log 2>&1 &
echo $! > .dev/api.pid

echo "[web] 启动 Vue 前端 http://localhost:5173 ..."
( cd web && npm install && npm run dev ) > .dev/web.log 2>&1 &
echo $! > .dev/web.pid

echo "[web-react] 启动 React 前端 http://localhost:5174 ..."
( cd web-react && npm install && npm run dev ) > .dev/web-react.log 2>&1 &
echo $! > .dev/web-react.pid

echo
echo "三个服务已在后台运行。"
echo "  日志：tail -f .dev/api.log   |   tail -f .dev/web.log   |   tail -f .dev/web-react.log"
echo "  首次运行的超级管理员密码输出在 .dev/api.log 中（grep -i password .dev/api.log）"
echo "  停止：./stop.sh"
