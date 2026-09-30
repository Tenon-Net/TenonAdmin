#!/usr/bin/env bash
# Dual-replica smoke test: verifies the batch-6 guarantees, plus (§7) the third-party
# integration module's, that **only surface when two replicas are online at the same time**.
# Running a single replica ten thousand times would never reveal these issues, so this script
# is the only real evidence for them.
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
# grade, and it happens silently). Prefer the job dashboard's node registry: every scheduler
# process heartbeats its WorkerId there, so we see both replicas even if login traffic was
# sticky to one. Fall back to decoding snowflake ids from the login log if the dashboard is empty.
WORKERS=$(curl -s "$BASE/api/v1/sys/job/dashboard" -H "Authorization: Bearer $ADMIN_TOKEN" \
  | python3 -c "
import sys,json
try:
    nodes = json.load(sys.stdin)['data']['nodes']
    print(' '.join(sorted({str(n.get('workerId', n.get('WorkerId', ''))) for n in nodes if n is not None})))
except Exception:
    print('')")
if [ -z "${WORKERS// }" ]; then
  WORKERS=$(curl -s "$BASE/api/v1/sys/log/login/page?Current=1&Size=50" -H "Authorization: Bearer $ADMIN_TOKEN" \
    | python3 -c "
import sys,json
items = json.load(sys.stdin)['data']['items']
print(' '.join(sorted({str((int(i['id']) >> 6) & 63) for i in items})))")
fi
WORKER_COUNT=$(echo "$WORKERS" | wc -w)
[ "$WORKER_COUNT" -ge 2 ] \
  && pass "$WORKER_COUNT distinct worker ids showed up ($WORKERS) -- the two replicas really are issuing IDs independently" \
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

echo "== 6. Scheduled jobs fire exactly once cluster-wide, and the standby takes over =="
# Both replicas run a scheduler, but only the lease holder scans (`if (!_isLeader) return null`),
# so a calm two-replica run has exactly one scanner. What this section really gates is the
# **failover**: kill the leader and, without the standby's lease takeover, the job simply stops
# running and no new log rows ever appear.
#
# The distinct-ScheduledTime check below is a cheap "did any occurrence fire twice" backstop --
# it is NOT what gates the claim CAS, despite what this comment used to say. Measured 2026-07-27:
# delete `&& j.NextRunTime == expected` from JobSchedulerService.ClaimAsync, rebuild both replicas,
# and this whole section still passes green. It cannot catch it, because the CAS defends the
# overlapping-leader window (old leader resumes from a GC pause after the standby took the lease),
# which no scripted run reproduces. The CAS's real gate is JobClaimTests -- mutate it there.
NODES_JSON=$(curl -s "$BASE/api/v1/sys/job/dashboard" -H "Authorization: Bearer $ADMIN_TOKEN")
NODE_COUNT=$(echo "$NODES_JSON" | python3 -c "
import sys,json
try: print(len(json.load(sys.stdin)['data']['nodes']))
except Exception: print(0)")
if [ "$NODE_COUNT" -lt 2 ]; then
  fail "Only $NODE_COUNT scheduler node(s) registered -- expected 2. Either a replica is down or its scheduler never started; the rest of this section cannot prove anything, so it is not skipped silently."
else
  pass "Both replicas registered as scheduler nodes ($NODE_COUNT)"

  # Randomize the code, like LOCK_ACCOUNT above. Deleting a job is a **soft** delete, so its Code
  # stays taken -- with a fixed code, the second run against the same database dies on 47002
  # (codeExists). That is the normal local loop: bring the stack up, run this, rebuild, run it again.
  JOB_CODE="smoke-tick-$RANDOM"
  JOB_BODY="{\"code\":\"$JOB_CODE\",\"name\":\"smoke tick\",\"handlerKind\":2,\"handlerName\":\"\",\"triggerKind\":2,\"intervalSeconds\":5,\"properties\":{\"url\":\"http://127.0.0.1:8080/health\"}}"
  JOB_RESP=$(curl -s -X POST "$BASE/api/v1/sys/job" -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $ADMIN_TOKEN" -d "$JOB_BODY")
  JOB_ID=$(echo "$JOB_RESP" | json data)
  if [ -z "$JOB_ID" ]; then
    # Print the envelope: "module disabled" was only ever a guess, and it sent the last person
    # looking in the wrong place -- the actual code (47002/47004/47009/...) says what happened.
    fail "Could not create the smoke job -- response: $JOB_RESP"
  else
    # Poll instead of a fixed sleep: create → first claim can lag a tick on a cold leader,
    # and a single 25s window once showed only 1 row on main while failover still worked.
    echo "  (waiting up to 40s for the 5s-interval job to fire at least 3 times...)"
    TOTAL=0
    DISTINCT=0
    for _wait in $(seq 1 20); do
      RUNS=$(curl -s --max-time 5 "$BASE/api/v1/sys/job/log/page?JobId=$JOB_ID&Size=50" -H "Authorization: Bearer $ADMIN_TOKEN" \
        | python3 -c "
import sys,json
try:
    items = json.load(sys.stdin)['data']['items']
    times = [i.get('scheduledTime') or i.get('ScheduledTime') for i in items]
    print(len(times), len(set(times)))
except Exception:
    print('0 0')")
      TOTAL=$(echo "$RUNS" | cut -d' ' -f1)
      DISTINCT=$(echo "$RUNS" | cut -d' ' -f2)
      [ "${TOTAL:-0}" -ge 3 ] && break
      sleep 2
    done
    [ "${TOTAL:-0}" -ge 3 ] \
      && pass "The job fired $TOTAL times" \
      || fail "Only ${TOTAL:-0} run(s) in ~40s -- a 5s-interval job should fire at least 3 times; the scheduler is not running"
    [ "${TOTAL:-0}" = "${DISTINCT:-0}" ] \
      && pass "All $TOTAL runs have distinct scheduled times -- no occurrence fired twice" \
      || fail "$TOTAL runs but only $DISTINCT distinct scheduled times -- some occurrence fired twice. Two leaders were scanning at once, or one leader claimed the same occurrence twice; start at the lease (sys_job_lock) and the claim (JobSchedulerService.ClaimAsync)"

    LEADER=$(echo "$NODES_JSON" | python3 -c "
import sys,json
nodes = json.load(sys.stdin)['data']['nodes']
print(next((n['nodeName'] for n in nodes if n['isLeader']), ''))")
    LEADER_SVC=$(docker compose ps --format '{{.Service}} {{.Name}}' 2>/dev/null | while read -r svc name; do
      case "$svc" in app|app2)
        wid=$(docker exec "$name" printenv TenonAdmin__Id__WorkerId 2>/dev/null)
        [ -n "$wid" ] && [ "${LEADER##*#}" = "$wid" ] && echo "$svc"
      ;; esac
    done | head -1)
    if [ -z "$LEADER_SVC" ]; then
      fail "Could not map the leader node '$LEADER' back to a compose service -- cannot test failover"
    else
      BEFORE=$(curl -s "$BASE/api/v1/sys/job/log/page?JobId=$JOB_ID&Size=1" -H "Authorization: Bearer $ADMIN_TOKEN" | json data.total)
      echo "  (stopping the leader '$LEADER_SVC', then waiting 50s for the standby to take over: lease 30s + heartbeat 10s)"
      docker compose stop "$LEADER_SVC" >/dev/null 2>&1
      sleep 50
      # After stop, Caddy may still briefly route to the dead upstream (empty body / 502). Retry
      # until we get a real JSON envelope so a proxy blip is not reported as "standby never took over".
      AFTER=""
      NEW_LEADER=""
      for _try in $(seq 1 15); do
        AFTER=$(curl -s --max-time 5 "$BASE/api/v1/sys/job/log/page?JobId=$JOB_ID&Size=1" -H "Authorization: Bearer $ADMIN_TOKEN" | json data.total)
        NEW_LEADER=$(curl -s --max-time 5 "$BASE/api/v1/sys/job/dashboard" -H "Authorization: Bearer $ADMIN_TOKEN" | python3 -c "
import sys,json
try:
    nodes = json.load(sys.stdin)['data']['nodes']
    print(next((n['nodeName'] for n in nodes if n.get('isLeader') or n.get('IsLeader')), ''))
except Exception:
    print('')" 2>/dev/null)
        [ -n "$AFTER" ] && [ -n "$NEW_LEADER" ] && break
        sleep 2
      done
      [ "${AFTER:-0}" -gt "${BEFORE:-0}" ] \
        && pass "New runs kept appearing after the leader went down ($BEFORE -> $AFTER)" \
        || fail "No new runs after the leader went down ($BEFORE -> $AFTER) -- the standby never took the lease, so jobs stop when one replica dies"
      [ -n "$NEW_LEADER" ] && [ "$NEW_LEADER" != "$LEADER" ] \
        && pass "Leadership moved from '$LEADER' to '$NEW_LEADER'" \
        || fail "Leader is still reported as '$NEW_LEADER' -- the lease was never taken over"
      docker compose start "$LEADER_SVC" >/dev/null 2>&1
    fi
    curl -s -X DELETE "$BASE/api/v1/sys/job/$JOB_ID" -H "Authorization: Bearer $ADMIN_TOKEN" >/dev/null
  fi
fi

echo "== 7. Integration module: an app's grant, enable and credential state reaches every replica immediately =="
# Same guarantee as section 1 (sessions), for a different table: itg_app / itg_app_grant /
# itg_app_credential are read straight from the database on every open-API request, with no
# in-process cache (docs/third-party-integration-implementation.md §4.3) -- so a change an
# admin makes against replica A must be visible to a partner's very next call, even when that
# call happens to land on replica B. Skipped, not failed, on a host without the module (its
# admin API 404s), so this script stays usable against a plain deployment too.
PROBE_CODE=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/integration/app/page?Current=1&Size=1" -H "Authorization: Bearer $ADMIN_TOKEN")
if [ "$PROBE_CODE" = "404" ]; then
  echo "  ⚠️  TenonAdmin.Integration is not enabled on this host, skipping (not counted as a pass)"
else
  APP_IP=$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$(docker compose ps -q app)" 2>/dev/null)
  APP2_IP=$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$(docker compose ps -q app2)" 2>/dev/null)
  if [ -z "$APP_IP" ] || [ -z "$APP2_IP" ]; then
    fail "Could not get both replicas' container IPs -- cannot test cross-replica propagation directly"
  else
    A="http://$APP_IP:8080"; B="http://$APP2_IP:8080"
    # 只读状态变更后的第一次响应;不得用重试掩盖短暂的旧授权。
    whoami_code() {
      curl -sS -o /dev/null -w '%{http_code}' --max-time 5 "$1/api/open/v1/whoami" -H "X-Api-Key: $2"
    }
    # 管理写入固定发往 A,同时验证 HTTP 和业务码;失败时不输出可能含新凭据的响应。
    itg_admin() {
      local response payload='{}'
      if [ $# -ge 3 ]; then payload="$3"; fi
      response=$(curl -fsS --max-time 10 -X "$1" "$A$2" -H 'Content-Type: application/json' \
        -H "Authorization: Bearer $ADMIN_TOKEN" -d "$payload") || return 1
      [ "$(echo "$response" | json code)" = "0" ] || return 1
      echo "$response"
    }
    # 第 6 段刚重启过一个副本;只在开始业务断言前等待就绪。
    for replica in "$A" "$B"; do
      for _try in $(seq 1 30); do
        curl -fsS --max-time 2 "$replica/health/ready" > /dev/null && break
        sleep 1
      done
    done
    ROOT_ORG=$(itg_admin GET /api/v1/sys/org/list | python3 -c "
import sys, json
orgs = json.load(sys.stdin)['data']
print(next(o['id'] for o in orgs if not o.get('parentId')))")
    ITG_CODE="smoke-itg-$RANDOM"
    ITG_ID=$(itg_admin POST /api/v1/integration/app \
      "{\"code\":\"$ITG_CODE\",\"name\":\"smoke\",\"ownerOrgId\":$ROOT_ORG}" | json data)
    KEY=$(itg_admin POST "/api/v1/integration/app/$ITG_ID/credentials" '{}' | json data.apiKey)
    if [ -z "$ITG_ID" ] || [ -z "$KEY" ]; then
      fail "Could not create a smoke integration app or issue its credential"
    else
      [ "$(whoami_code "$B" "$KEY")" = "403" ] \
        && pass "Replica B denies the ungranted whoami endpoint before any grant (403)" \
        || fail "Replica B did not deny the ungranted endpoint -- expected 403"

      if itg_admin PUT "/api/v1/integration/app/$ITG_ID/grants" '{"permissions":["GET:/api/open/v1/whoami"]}' > /dev/null; then
        [ "$(whoami_code "$B" "$KEY")" = "200" ] \
          && pass "A grant made on replica A is honored by replica B on its very next request" \
          || fail "Replica B rejected the first request after the grant on replica A"
      else
        fail "Grant on replica A failed"
      fi

      if itg_admin POST "/api/v1/integration/app/$ITG_ID/disable" '{}' > /dev/null; then
        [ "$(whoami_code "$B" "$KEY")" = "401" ] \
          && pass "Disabling the app on replica A is honored by replica B immediately (401)" \
          || fail "Replica B accepted the first request after disabling on replica A"
      else
        fail "Disable on replica A failed"
      fi

      # 先重新启用并证明这把凭据在两端有效,再独立验证撤销,避免停用掩盖撤销失效。
      if itg_admin POST "/api/v1/integration/app/$ITG_ID/enable" '{}' > /dev/null \
        && [ "$(whoami_code "$A" "$KEY")" = "200" ] \
        && [ "$(whoami_code "$B" "$KEY")" = "200" ]; then
        CRED_ID=$(itg_admin GET "/api/v1/integration/app/$ITG_ID/credentials" | json data.0.id)
        if [ -n "$CRED_ID" ] && itg_admin POST "/api/v1/integration/app/$ITG_ID/credentials/$CRED_ID/revoke" '{}' > /dev/null; then
          [ "$(whoami_code "$A" "$KEY")" = "401" ] \
            && pass "Revoking on replica A is honored by A on its first request" \
            || fail "Replica A accepted the credential it just revoked"
          [ "$(whoami_code "$B" "$KEY")" = "401" ] \
            && pass "Revoking on replica A is honored by B on its first request" \
            || fail "Replica B accepted a credential revoked on replica A"
        else
          fail "Credential revocation on replica A failed"
        fi
      else
        fail "Could not prove the enabled app's credential works on both replicas before revocation"
      fi

      itg_admin DELETE "/api/v1/integration/app/$ITG_ID" > /dev/null || fail "Smoke app cleanup failed"
    fi
  fi
fi

echo
if [ "$FAILURES" -eq 0 ]; then
  echo "✅ Dual-replica smoke test: all checks passed"
else
  echo "❌ $FAILURES assertion(s) failed"
  exit 1
fi
