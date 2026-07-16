#!/usr/bin/env bash
# Dual-replica smoke test: verifies the batch-6 guarantees that **only surface when two
# replicas are online at the same time**. Running a single replica ten thousand times would
# never reveal these issues, so this script is the only real evidence for this batch.
#
# Usage (bring the stack up first):
#   docker compose -f docker-compose.yml -f docker-compose.scale.yml up -d --build
#   scripts/smoke-multi-replica.sh [base-url]
#
# Every assertion targets a specific bug; failure messages spell out exactly "what it would
# look like if this weren't fixed."
set -uo pipefail

BASE="${1:-http://localhost:8080}"
ADMIN_PASSWORD="${TENON_ADMIN_PASSWORD:-Tenon@123456}"
FAILURES=0

pass() { echo "  ✅ $1"; }
fail() { echo "  ❌ $1"; FAILURES=$((FAILURES + 1)); }

# Note: don't write this as ${3:+-d "$3"} -- that expansion treats the inner quotes as
# literal characters and word-splits on spaces, which would shred the JSON.
api() {   # method path [body]
  if [ $# -ge 3 ]; then
    curl -s -X "$1" "$BASE$2" -H 'Content-Type: application/json' -d "$3"
  else
    curl -s -X "$1" "$BASE$2" -H 'Content-Type: application/json'
  fi
}
code() {  # method path [token] -> returns only the status code
  if [ $# -ge 3 ]; then
    curl -s -o /dev/null -w '%{http_code}' -X "$1" "$BASE$2" -H "Authorization: Bearer $3"
  else
    curl -s -o /dev/null -w '%{http_code}' -X "$1" "$BASE$2"
  fi
}
login() { api POST /api/v1/auth/login "{\"account\":\"$1\",\"password\":\"$2\"}"; }

# Dotted-path value lookup (numeric segments are indices): ... | json data.accessToken / data.items.0.ip
# Don't use eval('d$1') -- the single quotes inside the passed-in ['data'] would prematurely
# terminate the -c string, the resulting Python syntax error would get swallowed by 2>/dev/null,
# and it would silently return empty (the kind of bug that takes forever to track down).
json() {
  python3 -c '
import sys, json
doc = json.load(sys.stdin)
for part in sys.argv[1].split("."):
    doc = doc[int(part)] if part.isdigit() else doc[part]
print(doc if doc is not None else "")
' "$1" 2>/dev/null
}

# The JWT's sid claim (session id): needed for force-logout. Pad the base64url payload before decoding.
jwt_sid() {
  python3 - "$1" <<'PY'
import base64, json, sys
payload = sys.argv[1].split('.')[1]
payload += '=' * (-len(payload) % 4)
print(json.loads(base64.urlsafe_b64decode(payload))['sid'])
PY
}

echo "== 0. wait for readiness =="
# Must wait for **both** replicas to be ready. /health is also round-robined through Caddy,
# so "one successful probe" proves nothing -- it likely just happened to hit whichever replica
# came up first. (This is exactly how CI flipped over before: the first probe hit the
# already-ready app and broke out, then the immediate re-check hit app2 which had just
# started, and the run was declared "stack not up." Locally app2 always warms up early, so
# this never showed up there.)
# Only counts once STREAK consecutive checks come back Healthy -- under round-robin, this
# count is enough to cover every replica.
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
[ "$STREAK" -ge "$STREAK_NEEDED" ] || { echo "❌ Both replicas were not ready at the same time"; exit 1; }
pass "Both replicas are ready via Caddy"

echo "== 1. Force-logout takes effect immediately across replicas =="
# This would fail with in-process caching: force-logout on replica A only clears A's own
# memory, while B's session:{sid} entry is still there (TTL = refresh-token lifetime, on the
# order of days) -> with LB round-robin, roughly half the requests would still be let through,
# and they'd keep being let through for days.
ADMIN_TOKEN=$(login superAdmin "$ADMIN_PASSWORD" | json data.accessToken)
VICTIM_TOKEN=$(login superAdmin "$ADMIN_PASSWORD" | json data.accessToken)
[ -n "$ADMIN_TOKEN" ] && [ -n "$VICTIM_TOKEN" ] || { echo "❌ Login failed, can't proceed with the rest of the test"; exit 1; }

VICTIM_SID=$(jwt_sid "$VICTIM_TOKEN")

# **Warm up both replicas first**: session caching is read-through (only queries the DB and
# backfills on a miss), so if the victim session were only used once, just one replica would
# have it cached -- the other replica, having never cached it, would naturally return 401 on
# its own DB lookup after force-logout. That would make this assertion **pass for the wrong
# reason**, completely missing the in-process-cache hole (this has actually happened in
# practice). Run a full round so both replicas pick up session:{sid} into their own cache --
# only then is there actually something to "leak" on force-logout.
WARM_OK=0
for i in $(seq 1 8); do
  [ "$(code GET "/api/v1/sys/user/page?Current=1&Size=1" "$VICTIM_TOKEN")" = "200" ] && WARM_OK=$((WARM_OK + 1))
done
[ "$WARM_OK" -eq 8 ] \
  && pass "Victim session works on both replicas before force-logout (cache warmed up)" \
  || fail "Victim session was already unstable before force-logout ($WARM_OK/8) -- precondition not met"

curl -s -X DELETE "$BASE/api/v1/sys/session/$VICTIM_SID" -H "Authorization: Bearer $ADMIN_TOKEN" > /dev/null

STILL_OK=0
for i in $(seq 1 12); do   # round-robined across both replicas by the LB; leaks if even one replica still honors this session
  [ "$(code GET "/api/v1/sys/user/page?Current=1&Size=1" "$VICTIM_TOKEN")" = "200" ] && STILL_OK=$((STILL_OK + 1))
done
[ "$STILL_OK" -eq 0 ] \
  && pass "All 12 requests got 401 after force-logout (both replicas invalidated immediately)" \
  || fail "$STILL_OK/12 requests were still let through after force-logout -- the other replica is still admitting on a stale cache entry (classic symptom of in-process caching)"

[ "$(code GET "/api/v1/sys/user/page?Current=1&Size=1" "$ADMIN_TOKEN")" = "200" ] \
  && pass "Only the target session was kicked; the admin's session was unaffected" || fail "A session that shouldn't have been kicked got kicked too"

echo "== 2. Login lockout threshold doesn't double with replica count =="
# If each replica counts independently, the threshold effectively becomes N x MaxFailCount (default 5 -> 10 with two replicas).
LOCK_ACCOUNT="smoke-lock-$RANDOM"
for i in $(seq 1 5); do login "$LOCK_ACCOUNT" "wrong-password-$i" > /dev/null; done
LOCK_CODE=$(login "$LOCK_ACCOUNT" "wrong-password-6" | json code)
[ "$LOCK_CODE" = "40004" ] \
  && pass "Locked out after 5 failures (round-robined across both replicas) (40004)" \
  || fail "Still not locked out on the 6th attempt (got $LOCK_CODE, expected 40004) -- failure count isn't shared across replicas, so the threshold was doubled to 10"

echo "== 3. The two replicas use different snowflake worker ids =="
# Same worker id = colliding primary keys when issued in the same millisecond (data-corruption
# grade, and it happens silently). Decode the worker id out of the login log's row Id:
# workerId = (id >> 6) & 63 (per SnowflakeIdGenerator's bit layout).
WORKERS=$(curl -s "$BASE/api/v1/sys/log/login/page?Current=1&Size=50" -H "Authorization: Bearer $ADMIN_TOKEN" \
  | python3 -c "
import sys,json
items = json.load(sys.stdin)['data']['items']
print(' '.join(sorted({str((int(i['id']) >> 6) & 63) for i in items})))")
WORKER_COUNT=$(echo "$WORKERS" | wc -w)
[ "$WORKER_COUNT" -ge 2 ] \
  && pass "$WORKER_COUNT distinct worker ids showed up in the login log's row Ids ($WORKERS) -- the two replicas really are issuing IDs independently" \
  || fail "Only worker id(s) [$WORKERS] showed up -- both replicas share the same WorkerId, so same-millisecond issuance would collide on the primary key"

echo "== 4. The real client IP is captured after the reverse proxy =="
# Without parsing X-Forwarded-For, every request appears to come from the Caddy container's
# single IP: all users would share one rate-limit bucket, and the IP column in the login log
# would be nothing but proxy addresses (audit trail voided).
PROXY_IP=$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$(docker compose ps -q web)" 2>/dev/null)
LOG_IP=$(curl -s "$BASE/api/v1/sys/log/login/page?Current=1&Size=1" -H "Authorization: Bearer $ADMIN_TOKEN" | json data.items.0.ip)
if [ -z "$PROXY_IP" ]; then
  echo "  ⚠️  Could not get the Caddy container's IP, skipping this check (not counted as a pass)"
elif [ "$LOG_IP" != "$PROXY_IP" ] && [ -n "$LOG_IP" ]; then
  pass "The login log recorded the client IP ($LOG_IP), not the proxy IP ($PROXY_IP)"
else
  fail "The login log recorded the proxy IP ($LOG_IP) -- X-Forwarded-For was not honored, making IP-based rate limiting meaningless"
fi

echo "== 5. The rate-limit threshold is cluster-wide, not per replica =="
# If the count stays in-process, N replicas = N x the threshold (auth bucket defaults to
# 20/min, so two replicas would allow 40/min), silently halving the §14 brute-force baseline.
# Wait for the fixed window to roll over first, so it isn't polluted by the auth requests above.
echo "  (waiting 61s for the fixed window to roll over...)"
sleep 61
OK_COUNT=0; LIMITED=0
for i in $(seq 1 40); do
  C=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/auth/captcha")
  [ "$C" = "200" ] && OK_COUNT=$((OK_COUNT + 1))
  [ "$C" = "429" ] && LIMITED=$((LIMITED + 1))
done
if [ "$OK_COUNT" -le 20 ] && [ "$LIMITED" -gt 0 ]; then
  pass "Only $OK_COUNT of 40 auth requests were let through (<= the cluster threshold of 20), $LIMITED were rate-limited"
else
  fail "$OK_COUNT requests were let through (expected <= 20), $LIMITED were rate-limited -- the count isn't shared across replicas, so the threshold was doubled to 40"
fi

echo
if [ "$FAILURES" -eq 0 ]; then
  echo "✅ Dual-replica smoke test: all checks passed"
else
  echo "❌ $FAILURES assertion(s) failed"
  exit 1
fi
