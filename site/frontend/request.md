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

- [The Typed Client](/frontend/request) — `client.ts`'s two middlewares: auth and 401 refresh
- [Dev Proxy & CORS](/frontend/request) — why the request layer needs the dev proxy
- [Adapting to the Backend Response](/frontend/api-contract) — `unwrap`, `ApiError`, pagination helpers, and error text



---

<!-- TODO(rewrite): merged from client.md -->

# The Typed Client

```ts
const baseUrl = import.meta.env.VITE_API_BASE ?? ''
export const client = createClient<paths>({ baseUrl })
// Refresh-only client: no middlewares attached, so the refresh call's own 401 can't recurse.
const bare = createClient<paths>({ baseUrl })
```

Default `baseUrl` is empty — the schema's path keys already include `/api/v1`, and `/api` is same-origin (proxied to the backend in dev, reverse-proxied in production). `VITE_API_BASE` is only needed when the frontend and API are genuinely on different origins.

Two middlewares are registered on `client` (not on `bare` — see below):

## Auth middleware

```ts
const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = useUserStore().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    return request
  },
}
```

Reads the token from the store at request time (not captured once at module load), so it always injects the current access token — including one that was just refreshed.

## 401 refresh middleware, and why the request needs a clone to be replayed

This is the least obvious part of `client.ts`. The problem: a `Request`'s body is a stream that can only be read once. If a POST/PUT gets a 401, the flow needs to refresh the token and retry the *same request* — but by the time the response comes back, `fetch` has already consumed the original request's body. Retrying it as-is would send an empty body.

The fix is to clone the request **before** it's sent, while the body stream is still untouched:

```ts
const replayable = new WeakMap<Request, Request>()

const refreshMiddleware: Middleware = {
  onRequest({ request }) {
    // Only write requests need a replay copy — GET/HEAD have no body to lose.
    if (request.method !== 'GET' && request.method !== 'HEAD') replayable.set(request, request.clone())
    return request
  },
  async onResponse({ request, response }) {
    if (response.status !== 401) return response
    const url = request.url
    if (url.includes('/api/v1/auth/refresh') || url.includes('/api/v1/auth/login')) return response

    const ok = await refreshOnce()
    if (!ok) {
      useUserStore().clear()
      const { router } = await import('@/router')
      if (router.currentRoute.value.path !== '/login') router.replace('/login')
      return response
    }
    const base = replayable.get(request) ?? request
    const retry = new Request(base, { headers: new Headers(base.headers) })
    retry.headers.set('Authorization', `Bearer ${useUserStore().accessToken}`)
    return fetch(retry)
  },
}
```

`Request.clone()` tees the underlying body stream into two independent readable copies — the original goes out over the wire as usual, and the untouched clone sits in a `WeakMap` keyed by the original `Request` instance (openapi-fetch carries that same instance through to `onResponse`; the `WeakMap` entry is garbage-collected automatically once the request is done, no manual cleanup needed).

Step by step, on a 401:

1. **Skip the refresh/login endpoints themselves** — a 401 from `/auth/refresh` or `/auth/login` is a real credential failure, not an expired-token situation; retrying it through the refresh flow would loop.
2. **`refreshOnce()`** — a single-flight guard so that if several requests 401 at the same moment, only one `/auth/refresh` call goes out and all of them await the same promise:
   ```ts
   let refreshing: Promise<boolean> | null = null
   function refreshOnce(): Promise<boolean> {
     refreshing ??= doRefresh().finally(() => { refreshing = null })
     return refreshing
   }
   ```
3. **Refresh fails** (no refresh token, network error, non-zero `code`, or no `data`) — clear the session and redirect to `/login` (the router import is lazy, to avoid a static import cycle with `client.ts`).
4. **Refresh succeeds** — rebuild the request from the replayable clone (or the original request itself for GET/HEAD, which never needed cloning), stamp on the freshly-refreshed access token, and re-issue it with a **raw `fetch()`**, not `client.GET/POST(...)` again. Going back through `client` would re-run both middlewares on the retry, and a second 401 (say, the new token is rejected too) would recurse into another refresh attempt.

## Why `doRefresh` uses `bare`, not `client`

```ts
async function doRefresh(): Promise<boolean> {
  const user = useUserStore()
  if (!user.refreshToken) return false
  const { data, error } = await bare.POST('/api/v1/auth/refresh', {
    body: { refreshToken: user.refreshToken },
  })
  const env = data as { code?: number; data?: unknown } | undefined
  if (error || !env || env.code !== 0 || !env.data) return false
  user.setSession(env.data as Parameters<typeof user.setSession>[0])
  return true
}
```

`bare` is a second `openapi-fetch` client built from the same schema but with **no middlewares attached**. Calling the refresh endpoint through it means a failing refresh (e.g. the refresh token is itself expired and the endpoint answers 401) never re-enters `refreshMiddleware.onResponse` at all — there's no middleware pipeline on `bare` to recurse into. The URL check in `onResponse` (skipping `/auth/refresh`/`/auth/login`) is a second line of defense that also covers login failures called through `client`; the refresh call's own recursion-safety comes from being on `bare` in the first place.



---

<!-- TODO(rewrite): merged from proxy.md -->

# Dev Proxy & CORS

The typed client (`src/api/client.ts`) and `gen:api` both assume the browser is talking same-origin to `/api` and `/openapi`. Neither makes any cross-origin allowance — `client`'s `baseUrl` defaults to empty, and `gen:api` fetches `/openapi/v1.json` as a plain relative-looking URL. Locally, the backend runs on a different port (`:5100`) than the dev server (`:5173`), so something has to bridge that gap before either can work.

That something is `vite.config.ts`'s dev proxy:

```ts
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

server: {
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
  },
},
```

It forwards `/api/*` and `/openapi/*` requests from `:5173` to the backend, so the browser only ever sees one origin. The target defaults to `http://localhost:5100`; set `TENON_API_TARGET` before starting Vite to point at a backend running elsewhere.

Without this proxy, both the typed client's requests and `gen:api`'s schema fetch would go directly to the backend's origin — and the backend's CORS policy defaults to deny-all, so the browser (or `gen:api`'s fetch) would reject the response before it reached `unwrap` or `openapi-typescript`. The proxy is what makes the request layer's same-origin assumption true in dev; in production a reverse proxy plays the same role.

See [Project Structure & Startup](/frontend/structure) for the full dev-proxy config and sibling-package aliases.

