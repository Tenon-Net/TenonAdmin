# The HTTP request layer

The request layer is just two things: one openapi-fetch client and the two middlewares bolted onto it. Every method signature on that client is derived from the backend's OpenAPI contract, so paths, query params, request bodies, and response shapes are all typed end to end. The auth middleware does one thing — attach the token before a request goes out. The hard part is the second one: an expired token has to stay completely invisible to business code, so a 401 triggers an automatic refresh and replay, and the caller never sees a single error.

## Panorama

```text
后端 OpenAPI (/openapi/v1.json)
  │  npm run gen:api
  ▼
src/api/schema.d.ts        generated types (paths, do not hand-edit)
  │
  ▼
src/api/client.ts          typed openapi-fetch client + auth/refresh middleware
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

## The typed client and its two middlewares

```ts
const baseUrl = import.meta.env.VITE_API_BASE ?? ''
const rawTransport = globalThis.fetch
const client = createClient<paths>({ baseUrl, fetch: rawTransport })
// Refresh-only client: no middleware attached, so a 401 on the refresh request itself has no way to recurse.
const bare = createClient<paths>({ baseUrl, fetch: rawTransport })
```

`baseUrl` defaults to empty. The schema's path keys already carry `/api/v1`, and `/api` is same-origin; in dev Vite proxies it to the backend, in production a reverse proxy or the backend's own static hosting takes over. You only set `VITE_API_BASE` when the frontend and the API genuinely live on different origins.

`rawTransport` grabs the native `globalThis.fetch` and holds onto it. Both clients use it as their underlying transport, and the replay calls it directly too — not through `client` — so the two middlewares never run a second time on the replayed request.

Both middlewares are attached to `client` only, not to `bare` (see below), in the order `authMiddleware` then `refreshMiddleware`.

### The auth middleware

```ts
const authMiddleware: Middleware = {
  onRequest({ request }) {
    const token = useUserStore.getState().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    return request
  },
}
```

The token is read from the store at the moment the request goes out, not read once at module load and cached. That way every request picks up the freshest token, including one that was just refreshed. Reading happens through `useUserStore.getState()`, not the store's hook form: the middleware runs outside React's render, where you can't call a hook, and zustand's `getState()` reads the current value synchronously from outside a component.

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
3. **Refresh fails** (no refreshToken, network error, non-zero `code`, or missing `data`): clear the session, then `gotoLogin()` does a full-page `window.location.assign('/login')`. Using `window.location` rather than a router navigation means `client.ts` never imports the router, so there's no static circular dependency between them; a full reload after the token dies also wipes the stale in-memory state on the way out.
4. **Refresh succeeds**: rebuild the request from the clone saved before it went out, attach the freshly refreshed token, and replay it through the bare **`rawTransport(retry)`**. GET/HEAD had no clone, so the original request is used directly. Going through `client.GET/POST(...)` again is deliberately avoided: that would rerun both middlewares on the replay, and if the new token were also rejected, another 401 would recurse into the next refresh.

### Why `doRefresh` uses `bare`, not `client`

```ts
async function doRefresh(): Promise<boolean> {
  const user = useUserStore.getState()
  if (!user.refreshToken) return false

  const { data, error } = await bare.POST('/api/v1/auth/refresh', {
    body: { refreshToken: user.refreshToken },
  })
  const envelope = data as { code?: number; data?: unknown } | undefined
  if (error || !envelope || envelope.code !== 0 || !envelope.data) return false

  useUserStore.getState().setSession(envelope.data as Parameters<typeof user.setSession>[0])
  return true
}
```

`bare` is a second `openapi-fetch` client built from the same schema, but with no middleware attached. Running the refresh through `bare` can't recurse: even if the refresh itself fails — the `refreshToken` has also expired and the endpoint answers 401 — that 401 never reaches `refreshMiddleware.onResponse`, because `bare` has no middleware chain to recurse through. The URL check in `onResponse` that skips `/auth/refresh` and `/auth/login` is only a second line of defense, incidentally covering a login failure made through `client`. The real reason the refresh request can't recurse is that it's simply not on `client`'s middleware chain.

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

For the full server config and local aliases, see [Project structure and startup](/frontend-react/structure).
