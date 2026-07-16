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

**Previous:** [HTTP Request Layer](/frontend/request/)
**Next:** [Dev Proxy & CORS](/frontend/request/proxy)
