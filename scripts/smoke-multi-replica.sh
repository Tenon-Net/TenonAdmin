#!/usr/bin/env bash
# 双副本冒烟:验的是第六批那几条**只在两个副本同时在线时才暴露**的保证。
# 单副本跑一万遍也照不出这些问题,所以这个脚本是本批唯一的真实证据。
#
# 用法(先把栈起起来):
#   docker compose -f docker-compose.yml -f docker-compose.scale.yml up -d --build
#   scripts/smoke-multi-replica.sh [base-url]
#
# 每条断言都对着一个具体的 bug,失败信息里直说"没修好之前会是什么样"。
set -uo pipefail

BASE="${1:-http://localhost:8080}"
ADMIN_PASSWORD="${TENON_ADMIN_PASSWORD:-Tenon@123456}"
FAILURES=0

pass() { echo "  ✅ $1"; }
fail() { echo "  ❌ $1"; FAILURES=$((FAILURES + 1)); }

# 注意别写成 ${3:+-d "$3"} —— 那种展开会把内层引号当字面量再按空格切词,JSON 会被切碎。
api() {   # method path [body]
  if [ $# -ge 3 ]; then
    curl -s -X "$1" "$BASE$2" -H 'Content-Type: application/json' -d "$3"
  else
    curl -s -X "$1" "$BASE$2" -H 'Content-Type: application/json'
  fi
}
code() {  # method path [token] → 只回状态码
  if [ $# -ge 3 ]; then
    curl -s -o /dev/null -w '%{http_code}' -X "$1" "$BASE$2" -H "Authorization: Bearer $3"
  else
    curl -s -o /dev/null -w '%{http_code}' -X "$1" "$BASE$2"
  fi
}
login() { api POST /api/v1/auth/login "{\"account\":\"$1\",\"password\":\"$2\"}"; }

# 点分路径取值(数字段即下标):... | json data.accessToken / data.items.0.ip
# 别用 eval('d$1') —— 传进去的 ['data'] 里的单引号会把 -c 的字符串提前截断,
# Python 语法错误再被 2>/dev/null 吞掉,于是静默返回空(排查半天的那种)。
json() {
  python3 -c '
import sys, json
doc = json.load(sys.stdin)
for part in sys.argv[1].split("."):
    doc = doc[int(part)] if part.isdigit() else doc[part]
print(doc if doc is not None else "")
' "$1" 2>/dev/null
}

# JWT 的 sid claim(会话号):强退要用它。base64url 补齐后解 payload。
jwt_sid() {
  python3 - "$1" <<'PY'
import base64, json, sys
payload = sys.argv[1].split('.')[1]
payload += '=' * (-len(payload) % 4)
print(json.loads(base64.urlsafe_b64decode(payload))['sid'])
PY
}

echo "== 0. 等待就绪 =="
# 必须等**两个副本都**就绪。/health 也是经 Caddy 轮询的,所以"探一次成功"什么都不说明:
# 很可能只是轮到了先起来的那个副本。(CI 上就这么翻过车:第一次探测轮到已就绪的 app 于是 break,
# 紧接着的复查轮到刚启动的 app2,直接判"栈没起来"。本地因为 app2 早跑热了,永远照不出来。)
# 连续 STREAK 次都 Healthy 才算数 —— 轮询下这个次数足以覆盖到每个副本。
STREAK_NEEDED=8
STREAK=0
for i in $(seq 1 120); do
  if [ "$(curl -s "$BASE/health/ready" || true)" = "Healthy" ]; then
    STREAK=$((STREAK + 1))
    [ "$STREAK" -ge "$STREAK_NEEDED" ] && break
  else
    STREAK=0
    sleep 2
  fi
done
[ "$STREAK" -ge "$STREAK_NEEDED" ] || { echo "❌ 两个副本没有同时就绪"; exit 1; }
pass "两个副本经 Caddy 就绪"

echo "== 1. 强制下线跨副本立即生效 =="
# 进程内缓存下这条会挂:副本 A 强退只清了 A 自己的内存,B 那份 session:{sid} 还在
# (TTL = 刷新令牌寿命,天级)→ 经 LB 轮询时约一半请求仍然放行,而且一放就是好几天。
ADMIN_TOKEN=$(login superAdmin "$ADMIN_PASSWORD" | json data.accessToken)
VICTIM_TOKEN=$(login superAdmin "$ADMIN_PASSWORD" | json data.accessToken)
[ -n "$ADMIN_TOKEN" ] && [ -n "$VICTIM_TOKEN" ] || { echo "❌ 登录失败,后面没法测"; exit 1; }

VICTIM_SID=$(jwt_sid "$VICTIM_TOKEN")

# **先把两个副本都焐热**:会话缓存是读穿透的(未命中才查库并回填),受害会话若只被用过一次,
# 就只有一个副本缓存了它 —— 另一个副本从没缓存过,强退后它一查库自然就 401 了。
# 那样这条断言会**因为错误的理由通过**,进程内缓存的洞根本照不出来(实测踩过)。
# 打满一轮让两个副本都把 session:{sid} 收进各自的缓存,强退才有东西可"漏"。
WARM_OK=0
for i in $(seq 1 8); do
  [ "$(code GET "/api/v1/sys/user/page?Current=1&Size=1" "$VICTIM_TOKEN")" = "200" ] && WARM_OK=$((WARM_OK + 1))
done
[ "$WARM_OK" -eq 8 ] \
  && pass "受害会话强退前在两个副本上均可用(缓存已焐热)" \
  || fail "受害会话强退前就不稳定($WARM_OK/8)—— 前提不成立"

curl -s -X DELETE "$BASE/api/v1/sys/session/$VICTIM_SID" -H "Authorization: Bearer $ADMIN_TOKEN" > /dev/null

STILL_OK=0
for i in $(seq 1 12); do   # 经 LB 轮询打到两个副本;只要有一个副本还认这个会话就会漏
  [ "$(code GET "/api/v1/sys/user/page?Current=1&Size=1" "$VICTIM_TOKEN")" = "200" ] && STILL_OK=$((STILL_OK + 1))
done
[ "$STILL_OK" -eq 0 ] \
  && pass "强退后 12 次请求全部 401(两个副本都立刻失效)" \
  || fail "强退后仍有 $STILL_OK/12 次请求被放行 —— 另一个副本还在拿旧缓存放人(进程内缓存的典型症状)"

[ "$(code GET "/api/v1/sys/user/page?Current=1&Size=1" "$ADMIN_TOKEN")" = "200" ] \
  && pass "只踢掉了目标会话,管理员会话不受影响" || fail "把不该踢的会话也踢了"

echo "== 2. 登录锁定阈值不随副本数翻倍 =="
# 各副本各数各的话,阈值就成了 N × MaxFailCount(默认 5 → 两副本 10)。
LOCK_ACCOUNT="smoke-lock-$RANDOM"
for i in $(seq 1 5); do login "$LOCK_ACCOUNT" "wrong-password-$i" > /dev/null; done
LOCK_CODE=$(login "$LOCK_ACCOUNT" "wrong-password-6" | json code)
[ "$LOCK_CODE" = "40004" ] \
  && pass "5 次失败(轮询打到两个副本)后即锁定(40004)" \
  || fail "第 6 次仍未锁定(拿到 $LOCK_CODE,期望 40004)—— 失败计数没跨副本共享,阈值被翻倍成 10"

echo "== 3. 两个副本用不同的雪花机器号 =="
# 同号 = 同毫秒发号撞主键(数据损坏级,且今天是静默发生的)。
# 从登录日志的行 Id 里把机器号解出来:workerId = (id >> 6) & 63(SnowflakeIdGenerator 的位布局)。
WORKERS=$(curl -s "$BASE/api/v1/sys/log/login/page?Current=1&Size=50" -H "Authorization: Bearer $ADMIN_TOKEN" \
  | python3 -c "
import sys,json
items = json.load(sys.stdin)['data']['items']
print(' '.join(sorted({str((int(i['id']) >> 6) & 63) for i in items})))")
WORKER_COUNT=$(echo "$WORKERS" | wc -w)
[ "$WORKER_COUNT" -ge 2 ] \
  && pass "登录日志的行 Id 里出现了 $WORKER_COUNT 个不同的机器号($WORKERS)—— 两个副本确实各发各的号" \
  || fail "只出现了机器号 [$WORKERS] —— 两个副本共用同一个 WorkerId,同毫秒发号就会撞主键"

echo "== 4. 反向代理之后取到的是真实客户端 IP =="
# 不解析 X-Forwarded-For 的话,所有请求看起来都来自 Caddy 容器那一个 IP:
# 全体用户共享一个限流桶,登录日志里的 IP 列也全是代理地址(审计作废)。
PROXY_IP=$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$(docker compose ps -q web)" 2>/dev/null)
LOG_IP=$(curl -s "$BASE/api/v1/sys/log/login/page?Current=1&Size=1" -H "Authorization: Bearer $ADMIN_TOKEN" | json data.items.0.ip)
if [ -z "$PROXY_IP" ]; then
  echo "  ⚠️  拿不到 Caddy 容器 IP,跳过本条(不算通过)"
elif [ "$LOG_IP" != "$PROXY_IP" ] && [ -n "$LOG_IP" ]; then
  pass "登录日志记的是客户端 IP($LOG_IP),不是代理 IP($PROXY_IP)"
else
  fail "登录日志记的是代理 IP($LOG_IP)—— X-Forwarded-For 没被采信,按 IP 限流形同虚设"
fi

echo "== 5. 限流阈值是整个集群的,不是每副本一份 =="
# 计数留在进程内的话,N 个副本 = N × 阈值(认证桶默认 20/min,两副本就是 40/min),
# §14 的爆破基线被静默削半。等窗口翻篇后再打,免得被上面那些认证请求污染计数。
echo "  (等 61 秒让固定窗口翻篇…)"
sleep 61
OK_COUNT=0; LIMITED=0
for i in $(seq 1 40); do
  C=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/auth/captcha")
  [ "$C" = "200" ] && OK_COUNT=$((OK_COUNT + 1))
  [ "$C" = "429" ] && LIMITED=$((LIMITED + 1))
done
if [ "$OK_COUNT" -le 20 ] && [ "$LIMITED" -gt 0 ]; then
  pass "40 次认证请求里只放行了 $OK_COUNT 次(≤20 的集群阈值),$LIMITED 次被限"
else
  fail "放行了 $OK_COUNT 次(期望 ≤20)、被限 $LIMITED 次 —— 计数没跨副本共享,阈值被翻倍成 40"
fi

echo
if [ "$FAILURES" -eq 0 ]; then
  echo "✅ 双副本冒烟全部通过"
else
  echo "❌ $FAILURES 条断言失败"
  exit 1
fi
