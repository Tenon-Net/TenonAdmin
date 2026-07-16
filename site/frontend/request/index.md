# HTTP Request Layer

Every backend call in the frontend flows through one pipeline: a generated OpenAPI schema types the client, two middlewares handle auth and token refresh, and `unwrap` normalizes whatever shape the response comes back in before a view ever sees it. This page walks through each stage.

## Overview

```text
backend OpenAPI (/openapi/v1.json)
  │  npm run gen:api
  ▼
src/api/schema.d.ts        generated types (paths, do not hand-edit)
  │
  ▼
src/api/client.ts          typed openapi-fetch client + auth/refresh middlewares
  │
  ▼
src/api/index.ts           domain-grouped API functions, each: client.X(...).then(r => unwrap<T>(r))
  │
  ▼
views                       catch ApiError, display via translateError(err)
```

## Regenerating the contract: `gen:api`

```bash
npm run gen:api   # openapi-typescript http://localhost:5100/openapi/v1.json -o src/api/schema.d.ts
```

- The backend must be running first — the script fetches `/openapi/v1.json` from a live server (`http://localhost:5100` by default; see `web/vite.config.ts`'s `TENON_API_TARGET` for the dev proxy target if the backend runs elsewhere).
- `src/api/schema.d.ts` is a **generated artifact** — never hand-edit it. Change the backend endpoint/DTO and regenerate; hand edits are silently overwritten on the next run.
- `src/api/client.ts` types its `createClient<paths>()` call against this file, so every `client.GET/POST/PUT/DELETE` call is fully typed end to end — path params, query params, request body, and response shape all come from the backend's actual contract.

## In this section

- [The Typed Client](/frontend/request/client) — `client.ts`'s two middlewares: auth and 401 refresh
- [Dev Proxy & CORS](/frontend/request/proxy) — why the request layer needs the dev proxy
- [Adapting to the Backend Response](/frontend/request/backend) — `unwrap`, `ApiError`, pagination helpers, and error text

**Next:** [The Typed Client](/frontend/request/client)
