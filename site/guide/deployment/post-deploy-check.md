# Post-Deploy Self-Check

```bash
curl https://<your-domain>/health         # Healthy (liveness)
curl https://<your-domain>/health/ready   # Healthy (DB + cache both reachable)
curl -i https://<your-domain>/api/v1/ping # 401 = API routing works (this endpoint requires login)
```

Then open the frontend and log in once, to confirm the menu loads (which means the JWT secret, database, and seed data are all correct).

Note that `/openapi/v1.json` is **only mounted in the Development environment** — it's the contract source used by the frontend's `npm run gen:api`, not a production endpoint; a 404 in production is expected behavior.

**Previous:** [Multi-Replica Deployment](/guide/deployment/multi-replica)
