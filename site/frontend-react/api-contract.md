# Talking to the backend: response contract and error codes

A backend response arrives in one of two shapes, but a caller only ever wants one outcome: a usable value, or an error it can catch. The one function that reconciles them is `unwrap` — it resolves every response into a `T`, or throws an `ApiError`.

How a request goes out with its types and token belongs to the [request layer](/frontend-react/request); this picks up the moment a response lands back in your hands.

## `unwrap` and `ApiError`

The API functions in `web-react/src/api/index.ts` are hand-written. `gen:api` only produces the types in `schema.d.ts`; the bodies you write yourself, and almost all of them end in `.then(r => unwrap<T>(r))`. Here `r` is the raw result `client.GET/POST(...)` resolves to, always shaped `{ data, error, response }` — that is openapi-fetch's own convention, not something TenonAdmin invented. Paged endpoints end in `toPage`, which still calls `unwrap` internally. Only file downloads differ: they use `parseAs: 'blob'`, so the response is not an envelope at all and a plain `response.ok` check is enough.

`unwrap` is the single place that tolerates both response shapes. It collapses both into one `T`, or one thrown `ApiError`:

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
  if (!data || typeof data !== 'object') throw malformedResponse(response)
  const env = data as Envelope
  if (typeof env.code !== 'number') throw malformedResponse(response)
  if (env.code !== 0) {
    throw new ApiError(env.code, env.msgKey, env.args, env.message)
  }
  return env.data as T
}
```

The two shapes map onto the two backend layers:

- **Business envelope**: `Result<T>`, shaped `{ code, msgKey, args, data }`. A 2xx normal response uses it, and so do business-level failures like missing permission (403) or an invalid token (401). The test is simple: `code !== 0` throws an `ApiError` carrying the backend's numeric code and `msgKey`.
- **ProblemDetails**: ASP.NET's own error shape, `{ title, detail, ... }`, with no `code` field. It stands for the cases where business code never ran and the framework cut the request off first — model validation failure (400), an unhandled exception (500). These get wrapped into an `ApiError` built from the HTTP status plus `title`/`detail`.

The React template guards one step harder than the Vue one: when the body is not an object, or the envelope's `code` is not a number, `unwrap` throws `Malformed API response` rather than returning something ill-shaped as `data`. Failing loudly on a broken contract beats letting a downstream caller travel further holding an `undefined`.

`ApiError` carries enough to both display and branch on programmatically:

```ts
export class ApiError extends Error {
  code: number
  msgKey?: string
  args?: Record<string, unknown>
  constructor(code: number, msgKey?: string, args?: Record<string, unknown>, message?: string) {
    super(message ?? msgKey ?? `Error ${code}`)
    this.name = 'ApiError'
    this.code = code
    this.msgKey = msgKey
    this.args = args
  }
}
```

The view catches it like this:

```tsx
try {
  await userApi.remove(id)
} catch (e) {
  message.error(translateError(e))
}
```

How `translateError` turns `e` into that Chinese or English line is the subject of "Turning an error code into text" below.

## Paging helpers: `pageParams` / `toPage`

Every list endpoint repeats the same two conversions, so they are factored out:

```ts
export const pageParams = (p: { page: number; pageSize: number }) => ({ Current: p.page, Size: p.pageSize })

export function toPage<T>(res: Parameters<typeof unwrap>[0]): { items: T[]; total: number } {
  const p = unwrap<PagedList<T>>(res)
  if (!Array.isArray(p.items) || typeof p.total !== 'number') {
    throw new ApiError(res.response.status, undefined, undefined, 'Malformed paged API response')
  }
  return { items: p.items, total: p.total }
}
```

- **Request direction**: the frontend's `{ page, pageSize }` becomes the backend record's PascalCase `{ Current, Size }`. ASP.NET binding is actually case-insensitive on names, but this repo's convention is Pascal. `...pageParams(p)` spreads into each endpoint's own strongly-typed query object, sitting next to that endpoint's business filters.
- **Response direction**: the backend's `PagedList<T>` (`{ current, size, total, items }`) becomes `{ items, total }` — exactly the shape `DataTable`'s `request` expects (the layer that isolates pro-components). Along the way `toPage` checks that `items` is an array and `total` a number, throwing `Malformed paged API response` if not.

`unwrap`, `ApiError`, `pageParams`, and `toPage` are all exported together as the seam left for consumers. A consumer writing `api/<domain>.ts` imports from here instead of stuffing endpoints into `index.ts`. `index.ts` is upstream's territory: touch it and you resolve a conflict on every upstream merge. That is why these four exports stay even with no in-repo caller — don't strip them as unused.

## Turning an error code into text

The `ApiError` that `unwrap` throws carries a numeric `code` and a `msgKey`. Turning those two into a Chinese or English message is the least obvious link in the whole frontend/backend contract.

It all rests on one rule: **the backend never ships localized text**. Every business error is a numeric `ErrorCode` defined in `backend/src/TenonAdmin.Core/ErrorCode.cs`, and each enum member is tagged with a `[MsgKey("...")]` dotted path:

```csharp
/// <summary>文件超过大小上限;args 可携带 maxSizeMb</summary>
[MsgKey("error.file.tooLarge")]
FileTooLarge = 44002,
```

Why this way? If the backend returned a translated string, it would first have to know whether this user had switched to English, welding the error code to the language state so neither could change on its own. Instead the backend only gives the code, the frontend looks up the text, and the same error is written once in each of the two dictionaries — translation happens exactly once, and only on the frontend.

`GetMsgKey()` reflects once on first access and caches the keys into a `FrozenDictionary`. **A code with no `[MsgKey]` falls back to `error.code.{number}`** — an unmapped `44099` yields `error.code.44099`. The point of that fallback is that a missing annotation throws nothing; it just degrades to a string you can still trace.

The frontend's `error.*` namespace maps one-to-one onto those dotted paths, living in `web-react/src/locales/zh-CN.ts` and `en-US.ts`:

```ts
// src/locales/en-US.ts
error: {
  _fallback: 'Operation failed, please try again',
  file: {
    tooLarge: 'File too large',
    // ...
  },
}
```

The two sides actually meet in `web-react/src/utils/error.ts`:

```ts
export function translateError(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.msgKey && te(err.msgKey)) return t(err.msgKey)
    if (err.message) return err.message
  }
  if (err instanceof Error && err.message) return err.message
  return t('error._fallback')
}
```

The `te` here is not i18next's built-in `i18n.exists()`; it is a hand-written helper in `web-react/src/locales/index.ts`. They differ only on **subtree keys**, but that difference lands straight in the user's face. Should a backend msgKey happen to be a path pointing at a whole subtree, like `error.auth`, `i18n.exists('error.auth')` says `true`, while `t('error.auth')` returns an English debug line: `key 'error.auth' returned an object instead of string.` Using `exists()` would pop that debug line at the user as if it were the message. `te` is implemented as `i18n.exists(key) && typeof i18n.t(key, { returnObjects: true }) === 'string'`, returns `false` for a subtree, and falls back to the backend's own text. The Vue side never worries about this cell, because vue-i18n's `te` has that semantic natively; here it is assembled out of i18next, so it needs a deliberate guard.

The full path of a `FileTooLarge` (44002) therefore runs:

1. The backend throws `AdminException(ErrorCode.FileTooLarge)`.
2. The envelope carries `code: 44002`, `msgKey: "error.file.tooLarge"`, plus a non-localized `message` (a debug fallback only).
3. `translateError` finds `te('error.file.tooLarge')` → `true` → renders `t('error.file.tooLarge')` → `"文件超出大小限制"` / `"File too large"`.

How an error key is added and how the namespace is organized is no different from any other text key — that is [i18n](/frontend-react/i18n)'s job. A consumer adding text for its own error codes should not edit `zh-CN.ts` either; it drops keys under `locales/ext/<locale>/error.ts`, which deep-merge into the built-in namespace, again conflict-free.

::: warning Adding an error code is one change with two halves
Say the backend adds `[MsgKey("error.file.duplicateHash")] FileDuplicateHash = 44007`, but nobody adds `error.file.duplicateHash` to `zh-CN.ts` / `en-US.ts`. `te(...)` returns `false`, `translateError` falls back to `err.message`. And `err.message` defaults to the msgKey itself, because the backend sets `Result.Message = message ?? code.GetMsgKey()` and the `ApiError` constructor in turn uses `message ?? msgKey`. So the raw key `error.file.duplicateHash` is what pops on screen.

**Adding a backend `ErrorCode` and adding the matching frontend `error.xxx.yyy` key are two halves of the same change.**

Here is one difference from the Vue side that has to be spelled out: the backend's `ErrorCodeLocaleConsistencyTests` only scans `web/src/locales` — it does **not** cover `web-react/`. Miss a key and no test turns red for the React template; the only symptom is a bare msgKey on screen at runtime. The discipline is tighter on the React side precisely because no safety net catches it for you.
:::

## Regenerating the contract

`web-react/src/api/schema.d.ts` is not hand-written. It is the type set generated from a **running** backend instance by pulling `/openapi/v1.json`. Change a backend endpoint or DTO, and regenerate:

```bash
npm run gen:api
```

The word "running" is the whole point. `gen:api` fetches online through openapi-typescript; it does not scan code offline. So the backend has to be up first, with `http://localhost:5100/openapi/v1.json` reachable, before the command has a contract to pull. Start the backend with `dotnet run --project backend/samples/MinimalHost`, or bring up both ends with `dev.bat`.

::: warning
`schema.d.ts` is a generated artifact — don't hand-edit it. Any change is overwritten the next time `gen:api` runs. To adjust a type, change the backend endpoint or DTO and regenerate.
:::

How the envelope itself is assembled on the backend, and where the `Result<T>` that `unwrap` consumes gets wrapped on, belong to the [request pipeline](/backend/request-pipeline) page. `web-react/` and the Vue template talk to the same backend and the same `ErrorCode` set, so this contract layer is isomorphic across both; for why each template keeps its own copy instead of sharing a layer, see [frontend templates](/guide/frontend-templates).
