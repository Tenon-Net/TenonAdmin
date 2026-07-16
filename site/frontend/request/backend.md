# Adapting to the Backend Response

## `unwrap` and `ApiError`

Every generated API function ends in `.then(r => unwrap<T>(r))`. `unwrap` is the single place that tolerates the two response shapes the backend can send back, turning both into a plain `T` or a thrown `ApiError`:

```ts
export function unwrap<T>(res: { data?: unknown; error?: unknown; response: Response }): T {
  const { data, error, response } = res
  if (error !== undefined && error !== null) {
    const env = error as Envelope
    if (typeof env.code === 'number') {
      throw new ApiError(env.code, env.msgKey, env.args, env.message)
    }
    const pd = error as { title?: string; detail?: string }
    throw new ApiError(response.status, undefined, undefined, pd.title ?? pd.detail ?? response.statusText)
  }
  const env = (data ?? {}) as Envelope
  if (typeof env.code === 'number' && env.code !== 0) {
    throw new ApiError(env.code, env.msgKey, env.args, env.message)
  }
  return env.data as T
}
```

Two response shapes, corresponding to two different layers of the backend:

- **Business envelope** — `Result<T>` (`{ code, msgKey, args, data }`), returned on 2xx and on business-level failures like missing permission (403) or an invalid token (401): `code !== 0` throws `ApiError` carrying the backend's numeric code and `msgKey`.
- **ProblemDetails** — ASP.NET's own error shape (`{ title, detail, ... }`, no `code` field) for things the framework rejects before business code ever runs: model-validation failures (400) and unhandled exceptions (500). These get wrapped into an `ApiError` built from the HTTP status plus `title`/`detail`.

`ApiError` carries enough for both display and programmatic handling:

```ts
export class ApiError extends Error {
  code: number
  msgKey?: string
  args?: Record<string, unknown>
}
```

A view catches it like this:

```ts
try {
  await userApi.remove(id)
} catch (err) {
  message.error(translateError(err))
}
```

## Pagination helpers: `pageParams` / `toPage`

Every list endpoint repeats the same two conversions, so they're factored out:

```ts
const pageParams = (p: { page: number; pageSize: number }) => ({ Current: p.page, Size: p.pageSize })

function toPage<T>(res: Parameters<typeof unwrap>[0]): { items: T[]; total: number } {
  const p = unwrap<PagedList<T>>(res)
  return { items: p.items, total: p.total }
}
```

- **Request side** — the frontend's `{ page, pageSize }` become the backend's PascalCase `{ Current, Size }` query params (ASP.NET model binding is case-insensitive on names, but this codebase's convention is Pascal). `...pageParams(p)` spreads into each endpoint's own strongly-typed query object alongside its business filters.
- **Response side** — the backend's `PagedList<T>` (`{ current, size, total, items }`) becomes `{ items, total }`, the shape ProTable's `fetcher` contract expects.

## Error text: `translateError`

`ApiError.msgKey` is the link to display text — `translateError` (`src/utils/error.ts`) looks it up against the i18n catalog, falling back to `.message` and then to a generic fallback string. See [i18n](/frontend/i18n) for the `msgKey` convention and how backend codes map to translated copy.

**Previous:** [Dev Proxy & CORS](/frontend/request/proxy)

## Where to next

- [i18n](/frontend/i18n) — how `msgKey` becomes localized text
- [Permissions](/frontend/permission) — the `v-auth` directive and permission codes
- [Request Pipeline](/backend/request-pipeline) — the backend counterpart: auth, `[RolePermission]`, data scope, and the `Result<T>` envelope this page's `unwrap` consumes
