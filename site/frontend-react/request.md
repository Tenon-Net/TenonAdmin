# The HTTP request layer

Every API call goes through one openapi-fetch client and three middlewares, with method signatures generated from the backend's OpenAPI contract. The auth middleware attaches the token and CSRF proof, the refresh middleware replaces an expired token after 401 and replays the request, and the reauthentication middleware handles 403 + 40024 before a sensitive operation. React views call the API without reimplementing those security flows.

> Prerequisite: complete the [endpoint step in the tutorial](/frontend-react/getting-started#_4-add-the-real-endpoint-and-its-types).

## Trace one position request first

Open the browser network panel, then visit Position Preview. Select `/api/v1/sys/position/page` and confirm that the query contains `Current=1` and `Size=20`, the headers carry the current access token, and the response envelope has a numeric `code`. Clicking Refresh should add exactly one request to the same path.

The page assembled neither a URL nor a token. The complete request comes from `positionApi → client → middleware → fetch`; the sections below explain that chain from the generated contract outward.

## Panorama

```text
后端 OpenAPI (/openapi/v1.json)
  │  npm run gen:api
  ▼
src/api/schema.d.ts        generated types (paths, do not hand-edit)
  │
  ▼
src/api/client.ts          typed client + auth/refresh/reauth middleware
  │
  ▼
src/api/index.ts           API functions grouped by domain, one shape: client.X(...).then((r) => unwrap<T>(r))
  │
  ▼
views                       catch ApiError, display via translateError
```

The bottom two rows happen after the response comes back. Every API function in `src/api/index.ts` has the single shape `client.X(...).then((r) => unwrap<T>(r))`, where `unwrap` collapses the backend envelope into a result or throws `ApiError`; the view layer catches it and turns it into display text. Those two rows belong to [Talking to the backend response](/frontend-react/api-contract). Here the story stops at `client.ts`: how a request goes out carrying its types and its token.

## Regenerating the contract: `gen:api`

```bash
npm run gen:api   # openapi-typescript http://localhost:5100/openapi/v1.json -o src/api/schema.d.ts
```

- The backend has to be running first. The script pulls `/openapi/v1.json` from a live server, at the address hard-coded into this line of `package.json`, defaulting to `http://localhost:5100`.
- `src/api/schema.d.ts` is a **generated artifact** — don't hand-edit it. When a backend endpoint or DTO changes, regenerate; anything you edit by hand is silently overwritten on the next run.
- The client's `createClient<paths>()` uses that file as its type source. So on every `client.GET/POST/PUT/DELETE` call, the whole chain — path params, query params, request body, response shape — is typed off the backend's real contract.

<a id="the-typed-client-and-its-two-middlewares"></a>
## The typed client and its three middlewares

```ts
const baseUrl = import.meta.env.VITE_API_BASE ?? ''
const rawTransport = globalThis.fetch
const client = createClient<paths>({ baseUrl, fetch: rawTransport, credentials: 'include' })
// Refresh-only client: no middleware attached, so a 401 on the refresh request itself has no way to recurse.
const bare = createClient<paths>({ baseUrl, fetch: rawTransport, credentials: 'include' })
```

`baseUrl` defaults to empty. The schema's path keys already carry `/api/v1`, and `/api` is same-origin; in dev Vite proxies it to the backend, in production a reverse proxy or the backend's own static hosting takes over. You only set `VITE_API_BASE` when the frontend and the API genuinely live on different origins.

`rawTransport` grabs the native `globalThis.fetch` and holds onto it. Both clients use it as their underlying transport, and the replay calls it directly too — not through `client` — so the attached middleware chain never runs a second time on the replayed request.

`credentials: 'include'` lets cookie sessions carry the HttpOnly refresh cookie. All three middlewares attach only to `client`, in auth, refresh, reauthentication order; `bare` is reserved for refresh calls.

### The auth middleware

```ts
const authMiddleware: Middleware = {
  onRequest({ request }) {
    const token = useUserStore.getState().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    attachCsrf(request.headers)
    return request
  },
}
```

The token is read through `useUserStore.getState()` when the request leaves, so every request receives the latest value. Middleware runs outside React rendering and cannot call hooks; Zustand's `getState()` provides the synchronous component-external read. `attachCsrf()` also copies the readable `tenon_csrf` cookie into `X-Tenon-CSRF`; with no cookie, it adds no header.

### The 401 refresh middleware, and why the replay needs a clone

This is the least obvious part of `client.ts`. The problem is the `Request` body: it's a stream, readable only once. After a POST/PUT request is hit with a 401, the flow has to refresh the token first and then replay the same request — but by the time the response comes back, `fetch` has already drained the original body, and replaying it as-is would send an empty body.

The fix is to clone the request **before** it actually goes out, while the body stream is still untouched:

```ts
const replayable = new WeakMap<Request, Request>()

const refreshMiddleware: Middleware = {
  onRequest({ request }) {
    // Only write requests with a body need a replayable copy; GET/HEAD have no body to lose.
    if (request.method !== 'GET' && request.method !== 'HEAD') {
      replayable.set(request, request.clone())
    }
    return request
  },
  async onResponse({ request, response }) {
    if (response.status !== 401) return response
    if (request.url.includes('/api/v1/auth/refresh') || request.url.includes('/api/v1/auth/login')) {
      return response
    }

    if (!(await refreshOnce())) {
      useUserStore.getState().clear()
      gotoLogin()
      return response
    }

    const base = replayable.get(request) ?? request
    const retry = new Request(base, { headers: new Headers(base.headers) })
    retry.headers.set('Authorization', `Bearer ${useUserStore.getState().accessToken}`)
    return rawTransport(retry)
  },
}
```

`Request.clone()` splits the underlying body stream in two, each half independently readable. The original request goes out as usual; the untouched clone is stored in a `WeakMap` keyed by the original `Request` instance, and openapi-fetch carries that same instance all the way through to `onResponse`. Once the request finishes, the `WeakMap` entry is collected automatically — no manual cleanup.

On a 401, in order:

1. **Skip the refresh and login endpoints themselves**: a 401 from `/auth/refresh` or `/auth/login` is a genuine credential failure, not an expired token; feeding it into the refresh flow would loop forever.
2. **`refreshOnce()` coalesces concurrent refreshes**: when several requests hit a 401 at the same moment, only one `/auth/refresh` fires and everyone awaits the same promise:
   ```ts
   let refreshing: Promise<boolean> | null = null
   function refreshOnce(): Promise<boolean> {
     refreshing ??= doRefresh().finally(() => { refreshing = null })
     return refreshing
   }
   ```
3. **Refresh fails** (body mode has no refresh token, the network fails, `code` is non-zero, or `data` is absent): clear user and authorization state, then let `gotoLogin()` perform a full-page `window.location.assign('/login')`. `client.ts` therefore never imports the router and cannot form a static cycle. An empty local refresh token is normal in cookie mode because that token lives in an HttpOnly cookie.
4. **Refresh succeeds**: rebuild the request from the clone saved before it went out, attach the freshly refreshed token, and replay it through the bare **`rawTransport(retry)`**. GET/HEAD had no clone, so the original request is used directly. Going through `client.GET/POST(...)` again is deliberately avoided: that would rerun the middleware chain on the replay, and if the new token were also rejected, another 401 would recurse into the next refresh.

### Why `doRefresh` uses `bare`, not `client`

```ts
async function doRefresh(): Promise<boolean> {
  const user = useUserStore.getState()
  const cookie = isCookieSession(user)
  if (!cookie && !user.refreshToken) return false

  const headers: Record<string, string> = {}
  const csrf = readCsrfCookie()
  if (csrf) headers[AUTH_CSRF_HEADER] = csrf

  const { data, error } = await bare.POST('/api/v1/auth/refresh', {
    body: { refreshToken: cookie ? '' : user.refreshToken },
    headers,
  })
  const envelope = data as { code?: number; data?: unknown } | undefined
  if (error || !envelope || envelope.code !== 0 || !envelope.data) return false

  useUserStore.getState().setSession(envelope.data as Parameters<typeof user.setSession>[0])
  return true
}
```

`bare` is a second `openapi-fetch` client built from the same schema, but with no middleware attached. Running the refresh through `bare` can't recurse: even if the refresh itself fails — the `refreshToken` has also expired and the endpoint answers 401 — that 401 never reaches `refreshMiddleware.onResponse`, because `bare` has no middleware chain to recurse through. The URL check in `onResponse` that skips `/auth/refresh` and `/auth/login` is only a second line of defense, incidentally covering a login failure made through `client`. The real reason the refresh request can't recurse is that it's simply not on `client`'s middleware chain.

### The 403 reauthentication middleware

Some sensitive endpoints answer with 403 and business code 40024 to require another proof of identity. `reauthMiddleware` handles only that response, calls `requestReauth()`, and replays the saved request once after success. The replay carries `X-Tenon-Reauth-Retry: 1`, preventing a second 40024 from creating a loop. Cancellation or failure leaves the original response intact. It shares the request clone captured by the refresh middleware, so a sensitive operation with a body does not lose data on retry.

## The dev proxy and CORS

The typed client assumes the browser reaches `/api` same-origin: `client`'s `baseUrl` is empty, the request goes to what looks like a relative URL, and nothing about cross-origin is handled. `gen:api` is different — it never goes through the browser at all: Node fires the command straight at the hard-coded `http://localhost:5100/openapi/v1.json`, bypassing the dev proxy, so CORS never enters the picture. When the backend isn't on 5100, you edit that line in `package.json`; `TENON_API_TARGET` has no effect on it. In local dev the backend runs on `:5100` and the dev server on `:5174` — different ports, so something has to bridge the gap.

That bridge is the dev proxy in `vite.config.ts`:

```ts
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

server: {
  port: 5174,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
    '/hub': { target: apiTarget, changeOrigin: true, ws: true }, // SignalR notification hub
  },
},
```

It forwards `/api/*` and `/openapi/*` from `:5174` to the backend; `/hub` is the WebSocket channel for SignalR real-time notifications, and `ws: true` proxies the upgrade too. The browser only ever sees a single origin, `:5174`, so there's no cross-origin problem. The target defaults to `http://localhost:5100`; when the backend is elsewhere, set `TENON_API_TARGET` before starting Vite.

What happens without this proxy? The typed client's requests and the `gen:api` schema pull would both hit the backend's own origin directly. The backend's CORS defaults to deny-all, so the response is rejected by the browser (or by `gen:api`'s fetch) before it ever reaches `unwrap` or `openapi-typescript`. It's this proxy that makes the request layer's "same-origin" premise hold locally.

::: tip There is no proxy in production
The `npm run dev` proxy exists only during development. A production build is plain static files in `web-react/dist`, and how requests reach the backend is something you solve at deploy time: the backend hosts the frontend assets, or nginx/Caddy reverse-proxies them — both same-origin, no CORS to configure. Only when the frontend and backend are truly cross-origin — the frontend on a CDN, the backend on its own domain — do you touch `TenonAdmin:Api:Cors:AllowedOrigins`; see [Deployment route C: true cross-origin](/guide/deployment/route-c).
:::

## Verify the request path in the browser

After login, open the browser network panel and perform one protected read and one write. The read should carry the latest `Authorization` header; in cookie-session mode the write should also carry `X-Tenon-CSRF`. Expire the access token and repeat the request: the panel should show one refresh followed by a successful replay of the original request, even when several requests fail concurrently. If refresh fails, the Zustand session should clear and the browser should perform a full-page return to login rather than retain stale state and keep issuing 401 responses.

For the full server config and local aliases, see [Project structure and startup](/frontend-react/structure).
