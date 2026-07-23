#!/usr/bin/env bash
# stop.sh - 停止端口 5100（api）/ 5173（web）/ 5174（web-react）上的开发服务（对应 Windows 的 stop.bat）
cd "$(dirname "$0")"

for P in 5100 5173 5174; do
  pids=$(lsof -tiTCP:"$P" -sTCP:LISTEN -n -P 2>/dev/null || true)
  if [ -z "$pids" ]; then
    echo "[stop] 端口 $P 未运行"
    continue
  fi
  for pid in $pids; do
    name=$(ps -p "$pid" -o comm= 2>/dev/null | xargs -0 basename 2>/dev/null || echo '?')
    echo "[stop] 端口 $P -> 终止进程 $pid ($name)"
    kill "$pid" 2>/dev/null || true
    # 如果进程仍存活则强制终止（等同于 .bat 的 /F）。
    sleep 1
    kill -0 "$pid" 2>/dev/null && kill -9 "$pid" 2>/dev/null || true
  done
done

rm -f .dev/api.pid .dev/web.pid .dev/web-react.pid 2>/dev/null || true
echo 完成。
