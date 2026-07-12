#!/usr/bin/env bash
# dev.sh - 一键起后端 + 前端开发环境(macOS / Linux,对应 dev.bat)
# 后端 http://localhost:5100 (MinimalHost)   前端 http://localhost:5173 (Vite)
# 后端用 5100 而非 5000:macOS 的 AirPlay 接收器默认占着 5000。
# 用法:在仓库根目录执行  ./dev.sh   停止用  ./stop.sh
set -euo pipefail
cd "$(dirname "$0")"

API_PORT=5100
# vite proxy 读这个覆盖默认目标,保证前端反代到同一个后端端口(单一来源=本文件)。
export TENON_API_TARGET="http://localhost:$API_PORT"

if lsof -iTCP:"$API_PORT" -sTCP:LISTEN -n -P >/dev/null 2>&1; then
  echo "[!] 端口 $API_PORT 已被占用——后端可能已在跑。先 ./stop.sh 再试。"
  exit 1
fi

mkdir -p .dev

echo "[api] starting backend http://localhost:$API_PORT ..."
# --no-launch-profile:跳过 Properties/launchSettings.json(避免其 applicationUrl 盖掉下面的 URL)。
# 该 profile 顺带设的 Development 环境要手动补回(OpenAPI 是 dev-only)。
( cd backend && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:$API_PORT" \
    dotnet run --no-launch-profile --project samples/MinimalHost ) > .dev/api.log 2>&1 &
echo $! > .dev/api.pid

echo "[web] starting frontend http://localhost:5173 ..."
( cd web && npm install && npm run dev ) > .dev/web.log 2>&1 &
echo $! > .dev/web.pid

echo
echo "两个服务已在后台启动。"
echo "  日志:  tail -f .dev/api.log   |   tail -f .dev/web.log"
echo "  首启超管密码打印在 .dev/api.log(grep -i password .dev/api.log)"
echo "  停止:  ./stop.sh"
