#!/usr/bin/env bash
# dev.sh - one command to start backend + frontend dev environment (macOS / Linux, counterpart to dev.bat)
# Backend http://localhost:5100 (MinimalHost)   Frontend http://localhost:5173 (Vite)
# Backend uses 5100 instead of 5000: macOS's AirPlay receiver occupies 5000 by default.
# Usage: run  ./dev.sh  from the repo root   stop with  ./stop.sh
set -euo pipefail
cd "$(dirname "$0")"

API_PORT=5100
# The vite proxy reads this to override its default target, ensuring the frontend proxies to the same backend port (single source of truth = this file).
export TENON_API_TARGET="http://localhost:$API_PORT"

if lsof -iTCP:"$API_PORT" -sTCP:LISTEN -n -P >/dev/null 2>&1; then
  echo "[!] Port $API_PORT is already in use -- the backend may already be running. Try ./stop.sh first."
  exit 1
fi

mkdir -p .dev

echo "[api] starting backend http://localhost:$API_PORT ..."
# --no-launch-profile: skip Properties/launchSettings.json (avoid its applicationUrl overriding the URL below).
# The Development environment that profile also sets must be restored manually (OpenAPI is dev-only).
( cd backend && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:$API_PORT" \
    dotnet run --no-launch-profile --project samples/MinimalHost ) > .dev/api.log 2>&1 &
echo $! > .dev/api.pid

echo "[web] starting frontend http://localhost:5173 ..."
( cd web && npm install && npm run dev ) > .dev/web.log 2>&1 &
echo $! > .dev/web.pid

echo
echo "Both services are now running in the background."
echo "  Logs:  tail -f .dev/api.log   |   tail -f .dev/web.log"
echo "  The first-run super-admin password is printed in .dev/api.log (grep -i password .dev/api.log)"
echo "  Stop:  ./stop.sh"
