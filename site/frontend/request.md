# HTTP Request Layer

Every backend call in the frontend flows through the same pipeline: an openapi-fetch client typed from the backend's OpenAPI contract, plus two middlewares — one that attaches the token to the request, one that refreshes the token on a 401 and replays the original request. This page covers how that pipeline is assembled, why the two middlewares are written the way they are, and why local-dev requests reach the backend without any CORS setup.

## The big picture

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
src/api/index.ts           domain-grouped API functions, all shaped: client.X(...).then(r => unwrap<T>(r))
  │
  ▼
views                       catch ApiError, display via translateError(err)
```

The bottom two rows of that diagram — `src/api/index.ts`'s `unwrap` and the view layer's `translateError` — are what happens *after* the response comes back: how the backend's two response shapes are collapsed into one result, and how an error code becomes display text. Those are split off into [Backend Contract & Error Codes](/frontend/api-contract). This page stops at `client.ts` — i.e. how a request goes out, typed and carrying its token.

## Regenerating the contract: `gen:api`

```bash
npm run gen:api   # openapi-typescript http://localhost:5100/openapi/v1.json -o src/api/schema.d.ts
```

- The backend must be running first — the script fetches `/openapi/v1.json` from a live server (`http://localhost:5100` by default; if the backend runs elsewhere, see the dev-proxy target behind `TENON_API_TARGET` in `web/vite.config.ts`).
- `src/api/schema.d.ts` is a **generated artifact** — never hand-edit it — change the backend endpoint/DTO and regenerate; hand edits are silently overwritten on the next run.
- `src/api/client.ts`'s `createClient<paths>()` uses this file as its type source, so every `client.GET/POST/PUT/DELETE` call is typed end to end — path params, query params, request body, and response shape all derive from the backend's actual contract.

## The typed client and its two middlewares

```ts
const baseUrl = import.meta.env.VITE_API_BASE ?? ''
export const client = createClient<paths>({ baseUrl })
// Refresh-only client: no middlewares attached, so the refresh call's own 401 has no way to recurse.
const bare = createClient<paths>({ baseUrl })
```

`baseUrl` defaults to empty — the schema's path keys already include `/api/v1`, and `/api` is same-origin (proxied to the backend in dev, reverse-proxied or self-hosted by the backend in production). `VITE_API_BASE` is only needed when the frontend and the API are genuinely cross-origin.

Two middlewares are registered on `client` (not on `bare` — see below):

### Auth middleware

```ts
const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = useUserStore().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    return request
  },
}
```

The token is read from the store at request time (not read once and cached at module load), so every request gets the current token — including one that was just refreshed.

### The 401 refresh middleware, and why replay needs a clone

This is the least obvious part of `client.ts`. The problem: a `Request`'s body is a stream that can only be read once. After a POST/PUT is hit with a 401, the flow needs to refresh the token and replay the *same request* — but by the time the response comes back, `fetch` has long since consumed the original request's body, and replaying it as-is would send an empty body.

The fix is to clone the request **before** it actually goes out, while the body stream is still untouched:

```ts
const replayable = new WeakMap<Request, Request>()

const refreshMiddleware: Middleware = {
  onRequest({ request }) {
    // Only write requests with a body need a replay copy — GET/HEAD have no body to lose.
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

`Request.clone()` tees the underlying body stream into two independently readable copies — the original goes out over the wire as usual, and the untouched clone is stashed in a `WeakMap` keyed by the original `Request` instance (openapi-fetch carries that same instance all the way through to `onResponse`; once the request finishes, the `WeakMap` entry is garbage-collected automatically, no manual cleanup).

On a 401, in order:

1. **Skip the refresh/login endpoints themselves** — a 401 from `/auth/refresh` or `/auth/login` is a genuine credential failure, not an expired token; feeding it into the refresh flow too would loop.
2. **`refreshOnce()`** — single-flight coalescing: if several requests 401 at the same moment, only one `/auth/refresh` call goes out, and they all await the same promise:
   ```ts
   let refreshing: Promise<boolean> | null = null
   function refreshOnce(): Promise<boolean> {
     refreshing ??= doRefresh().finally(() => { refreshing = null })
     return refreshing
   }
   ```
3. **Refresh fails** (no refresh token, network error, non-zero `code`, or no `data`) — clear the session and redirect to `/login` (the router is lazily imported to avoid a static circular dependency with `client.ts`).
4. **Refresh succeeds** — rebuild the request from the clone stashed before it was sent (GET/HEAD never cloned, so the original request is used directly), stamp on the freshly-refreshed token, and replay it with a **raw `fetch()`** — not another `client.GET/POST(...)`. Going back through `client` would re-run both middlewares on this replay, and if the new token were also rejected (another 401), it would recurse into the next round of refresh.

### Why `doRefresh` uses `bare`, not `client`

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

`bare` is a second `openapi-fetch` client built from the same schema but with **no middlewares attached**. Routing the refresh request through `bare` means a failed refresh (say, the refresh token itself has expired and the endpoint answers 401 too) never re-enters `refreshMiddleware.onResponse` at all — there's no middleware chain on `bare` to recurse into. The URL check in `onResponse` that skips `/auth/refresh`/`/auth/login` is a second line of defense, one that incidentally also covers login failures called through `client`; the refresh request's own recursion-safety comes, fundamentally, from it not being on `client`'s middleware chain in the first place.

## Dev proxy & CORS

Both the typed client (`src/api/client.ts`) and `gen:api` assume the browser is talking same-origin to `/api` and `/openapi` — `client`'s `baseUrl` defaults to empty, and `gen:api` fetches `/openapi/v1.json` via a URL that looks relative too; neither does any cross-origin handling. In local dev the backend runs on `:5100` and the dev server on `:5173`, different ports, so something has to bridge that gap before either can work.

That something is the dev proxy in `vite.config.ts`:

```ts
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

server: {
  port: 5173,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
  },
},
```

It forwards `/api/*` and `/openapi/*` requests on `:5173` to the backend, so the browser only ever sees one origin (`:5173`) — no cross-origin problem to speak of. The target defaults to `http://localhost:5100`; if the backend runs elsewhere, set `TENON_API_TARGET` before starting Vite.

Without this proxy, both the typed client's requests and `gen:api`'s schema fetch would hit the backend's origin directly — and the backend's CORS defaults to deny-all, so the browser (or `gen:api`'s fetch) would reject the response before it ever reached `unwrap` or `openapi-typescript`. It's this proxy that makes the request layer's "same-origin" assumption hold in local dev.

::: tip There's no proxy in production
The `npm run dev` proxy exists only during development. A production build's `web/dist` is plain static files, and how requests reach the backend is something you solve at deploy time: the backend serving the frontend build alongside it, or an nginx/Caddy reverse proxy — both same-origin, no CORS needed. Only when the frontend and backend are genuinely cross-origin (frontend on a CDN, backend on its own domain) do you touch `TenonAdmin:Api:Cors:AllowedOrigins`; for that setup, see [Deployment Route C: Genuinely Cross-Origin](/guide/deployment/route-c).
:::

For the full proxy config and the sibling-package dev aliases, see [Project Structure & Startup](/frontend/structure).
