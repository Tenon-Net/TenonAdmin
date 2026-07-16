# Multi-Replica Deployment

Horizontal scaling. Before starting a second replica, all **four** of the following are mandatory — skip any one and the system won't error, it'll just start quietly doing the wrong thing.

```bash
# The repo has a ready-made two-replica overlay, the same one used in CI
docker compose -f docker-compose.yml -f docker-compose.scale.yml up -d --build
bash scripts/smoke-multi-replica.sh http://localhost:8080   # verifies each of the guarantees below
```

## ① Redis is a **prerequisite**, not an optional optimization

An in-process cache means replica A's invalidations **never reach** replica B. The result isn't "a bit slower" — it's security features failing outright:

| Symptom | Detail |
|---|---|
| **Forced logout silently fails (the worst one)** | The session cache's TTL matches the **refresh-token lifetime** (days). Force a logout on A → the DB records the revocation and A clears its own memory; **B's copy is still there**, still considers the session active, so roughly half the requests through a load balancer sail through as normal — for **days**. |
| **Permissions persist after revocation** | The permission/data-scope cache defaults to 20 minutes. A user whose access was revoked still has it on another replica; the data scope also still feeds SqlSugar's global query filter — they **keep seeing other orgs' data**. |
| **Lockout/rate-limit thresholds effectively multiply** | Login failure counts and rate-limit counts are tracked per replica: `MaxFailCount=5` becomes 10 across two replicas, a 20/min auth bucket becomes 40/min. |
| **CAPTCHA verification always fails** | A one-time ticket issued on A and verified on B won't be found — B doesn't have that key. |

Set `Cache:Provider=Redis` + `Cache:RedisConnectionString` and **all of this is fixed automatically** (invalidation goes through the shared cache key space, not an event bus) — zero changes to business code.

## ② Every replica must have a **distinct `WorkerId`**

The same value means IDs generated in the same millisecond collide — a data-corruption-level bug. The kernel **no longer stays silent about this**: if Redis is configured (= clear multi-instance intent) but `TenonAdmin:Id:WorkerId` isn't set explicitly → **startup fails outright**.

- **compose**: `--scale app=2` can't give each replica a different environment variable, so write **multiple explicit `app` services** instead (see `docker-compose.scale.yml`, each with its own `WorkerId`).
- **k8s**: use a **StatefulSet** and inject `WorkerId` from the pod name's ordinal (`app-0`/`app-1`). A Deployment's random pod names can't give you a stable ordinal.

## ③ The reverse proxy must configure `ForwardedHeaders`

See the section above. Without it, both replicas only ever see the load balancer's single IP — per-IP rate limiting is meaningless, and the audit log's IP column is just the proxy's address.

## ④ On a cold start, **bring up one replica first**

CodeFirst table creation and seeding are "check-then-insert" — **not atomic**. If two replicas start for the first time simultaneously, one of them will crash on a unique-key collision.

- **compose**: have the second replica depend on `depends_on: app: condition: service_healthy` (that's exactly what `docker-compose.scale.yml` does) — no code required.
- **k8s**: use an init job / migration job to build the schema first, then open up the replicas.

## Not yet solved: the upload directory must be a **shared, writable volume**

`LocalFileStorage` / `ChunkStorage` write to **local disk**. In compose, the named volume is naturally shared across both replicas; but **on k8s, if each pod gets its own PVC, a file uploaded on A returns 404 on B**, and chunked uploads fail outright with `ChunkMissing` (chunks scattered across different pods can never be merged). Multi-replica deployments need an **RWX (ReadWriteMany)** shared volume mounted at the upload root, or should replace `IFileStorage` up front with object storage (S3/OSS).

**Previous:** [Route D: Docker](/guide/deployment/route-d)
**Next:** [Post-Deploy Self-Check](/guide/deployment/post-deploy-check)
