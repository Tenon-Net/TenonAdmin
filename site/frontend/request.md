# HTTP Request Layer

Every API call goes through one openapi-fetch client and three middlewares, with method signatures generated from the backend's OpenAPI contract. The auth middleware attaches the token and CSRF proof, the refresh middleware replaces an expired token after 401 and replays the request, and the reauthentication middleware handles 403 + 40024 before a sensitive operation. Views call the API without reimplementing those security flows.

> Prerequisite: complete the [endpoint step in the tutorial](/frontend/getting-started#_4-add-the-real-endpoint-and-its-types).

## Trace one position request first

Open the browser network panel, then visit Position Preview. Select `/api/v1/sys/position/page` and confirm three facts: the query contains `Current=1` and `Size=20`, the headers carry the current access token, and the response envelope has a numeric `code`. Clicking Refresh should add exactly one request to the same path.

The view assembled neither a URL nor a token, yet the network request is complete. The missing span is `positionApi → client → middleware → fetch`; the sections below expand it from the generated contract outward.

## The big picture

```text
backend OpenAPI (/openapi/v1.json)
  │  npm run gen:api
  ▼
src/api/schema.d.ts        generated types (paths, do not hand-edit)
  │
  ▼
src/api/client.ts          typed client + auth/refresh/reauth middlewares
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

<a id="the-typed-client-and-its-two-middlewares"></a>
## The typed client and its three middlewares

```ts
const baseUrl = import.meta.env.VITE_API_BASE ?? ''
export const client = createClient<paths>({ baseUrl, credentials: 'include' })
// Refresh-only client: no middlewares attached, so the refresh call's own 401 has no way to recurse.
const bare = createClient<paths>({ baseUrl, credentials: 'include' })
```

`baseUrl` defaults to empty — the schema's path keys already include `/api/v1`, and `/api` is same-origin (proxied to the backend in dev, reverse-proxied or self-hosted by the backend in production). `VITE_API_BASE` is only needed when the frontend and the API are genuinely cross-origin.

`credentials: 'include'` lets cookie sessions carry the HttpOnly refresh cookie in same-origin deployments and in cross-origin deployments that explicitly allow credentials. All three middlewares attach only to `client`; `bare` is reserved for refresh calls.

### Auth middleware

```ts
const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = useUserStore().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    if (isMutating(request.method)) attachCsrf(request.headers)
    return request
  },
}
```

The token is read when the request leaves, so every request receives the latest value. State-changing requests such as `POST`, `PUT`, `PATCH`, and `DELETE` also copy the readable `tenon_csrf` cookie into `X-Tenon-CSRF`. JavaScript cannot read the HttpOnly refresh cookie itself; the browser carries it through `credentials`.

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
3. **Refresh fails** (body mode has no refresh token, the network fails, `code` is non-zero, or `data` is absent) — clear user, authorization, and dynamic-route state, then redirect to `/login`. An empty local refresh token is normal in cookie mode because the token lives in an HttpOnly cookie.
4. **Refresh succeeds** — rebuild the request from the clone stashed before it was sent (GET/HEAD never cloned, so the original request is used directly), stamp on the freshly-refreshed token, and replay it with a **raw `fetch()`** — not another `client.GET/POST(...)`. Going back through `client` would rerun the middleware chain on this replay, and if the new token were also rejected (another 401), it would recurse into the next round of refresh.

### Why `doRefresh` uses `bare`, not `client`

```ts
async function doRefresh(): Promise<boolean> {
  const user = useUserStore()
  if (!user.cookieSession && !user.refreshToken) return false
  const headers: Record<string, string> = {}
  const csrf = readCookie(CSRF_COOKIE)
  if (csrf) headers[CSRF_HEADER] = csrf
  const { data, error } = await bare.POST('/api/v1/auth/refresh', {
    body: user.cookieSession ? { refreshToken: '' } : { refreshToken: user.refreshToken },
    headers,
  })
  const env = data as { code?: number; data?: unknown } | undefined
  if (error || !env || env.code !== 0 || !env.data) return false
  user.setSession(env.data as Parameters<typeof user.setSession>[0])
  return true
}
```

`bare` is a second `openapi-fetch` client built from the same schema but with **no middlewares attached**. Routing the refresh request through `bare` means a failed refresh (say, the refresh token itself has expired and the endpoint answers 401 too) never re-enters `refreshMiddleware.onResponse` at all — there's no middleware chain on `bare` to recurse into. The URL check in `onResponse` that skips `/auth/refresh`/`/auth/login` is a second line of defense, one that incidentally also covers login failures called through `client`; the refresh request's own recursion-safety comes, fundamentally, from it not being on `client`'s middleware chain in the first place.

### The 403 reauthentication middleware

Some sensitive endpoints answer with 403 and business code 40024 to require another proof of identity. `reauthMiddleware` intercepts only that response, calls `requestReauth()`, and replays the saved request once after success. The replay carries `X-Tenon-Reauth-Retry: 1`, preventing another 40024 from creating a loop. Cancellation or failed reauthentication returns the original response to the caller. Because it shares the request copy captured by the refresh middleware, a sensitive request with a body does not lose that body on retry.

## Dev proxy & CORS

The typed client (`src/api/client.ts`) assumes same-origin browser access to `/api`: `client` has an empty `baseUrl` and does no cross-origin handling. `gen:api` is a Node command that connects directly to the fixed `http://localhost:5100/openapi/v1.json`; it bypasses the browser and dev proxy, so CORS does not apply. If the backend does not run on `:5100`, edit the package script because `TENON_API_TARGET` does not affect generation. Locally the backend uses `:5100` and the Vue dev server defaults to `:5175`; use the URL printed by Vite if it differs.

That something is the dev proxy in `vite.config.ts`:

```ts
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

server: {
  port: 5175,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
  },
},
```

It forwards `/api/*` and `/openapi/*` requests on `:5175` to the backend, so the browser sees only the Vue dev-server origin. The target defaults to `http://localhost:5100`; if the backend runs elsewhere, set `TENON_API_TARGET` before starting Vite.

Without this proxy, both the typed client's requests and `gen:api`'s schema fetch would hit the backend's origin directly — and the backend's CORS defaults to deny-all, so the browser (or `gen:api`'s fetch) would reject the response before it ever reached `unwrap` or `openapi-typescript`. It's this proxy that makes the request layer's "same-origin" assumption hold in local dev.

::: tip There's no proxy in production
The `npm run dev` proxy exists only during development. A production build's `web/dist` is plain static files, and how requests reach the backend is something you solve at deploy time: the backend serving the frontend build alongside it, or an nginx/Caddy reverse proxy — both same-origin, no CORS needed. Only when the frontend and backend are genuinely cross-origin (frontend on a CDN, backend on its own domain) do you touch `TenonAdmin:Api:Cors:AllowedOrigins`; for that setup, see [Deployment Route C: Genuinely Cross-Origin](/guide/deployment/route-c).
:::

## Verify the request path in the browser

After login, open the browser network panel and perform one protected read and one write. The read should carry the latest `Authorization` header; in cookie-session mode the write should also carry `X-Tenon-CSRF`. Expire the access token and repeat the request: the panel should show one refresh followed by a successful replay of the original request. Several concurrent failures should still produce only one refresh. If refresh fails, the session should clear and the browser should return to login rather than keep issuing 401 responses.

For the full proxy config and the sibling-package dev aliases, see [Project Structure & Startup](/frontend/structure).
