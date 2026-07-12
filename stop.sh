#!/usr/bin/env bash
# stop.sh - 停掉 5100(api)/ 5173(web)端口上的开发服务(对应 stop.bat)
cd "$(dirname "$0")"

for P in 5100 5173; do
  pids=$(lsof -tiTCP:"$P" -sTCP:LISTEN -n -P 2>/dev/null || true)
  if [ -z "$pids" ]; then
    echo "[stop] port $P not running"
    continue
  fi
  for pid in $pids; do
    name=$(ps -p "$pid" -o comm= 2>/dev/null | xargs -0 basename 2>/dev/null || echo '?')
    echo "[stop] port $P -> killing pid $pid ($name)"
    kill "$pid" 2>/dev/null || true
    # 还活着就强杀(等同 .bat 的 /F)。
    sleep 1
    kill -0 "$pid" 2>/dev/null && kill -9 "$pid" 2>/dev/null || true
  done
done

rm -f .dev/api.pid .dev/web.pid 2>/dev/null || true
echo done.
