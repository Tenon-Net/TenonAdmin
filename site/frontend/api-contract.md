# Adapting to the Backend: Response Contract & Error Codes

Every generated API function has to end by turning the backend's response into either a usable value or a displayable error. This page covers how that collapse works: how `unwrap` tolerates the two response shapes, how pagination is converted, and how a numeric error code ends up as the sentence the user actually reads.

## `unwrap` and `ApiError`

Most of the hand-written functions in `src/api/index.ts` land on `.then(r => unwrap<T>(r))` (`gen:api` only generates `schema.d.ts`'s types — the functions themselves are hand-written). That `r` is the raw result resolved straight from `client.GET/POST(...)`, always shaped `{ data, error, response }` — that's openapi-fetch's own convention, not something TenonAdmin invented. Paged endpoints land on `toPage`, which calls `unwrap` internally; the one exception is file downloads, which use `parseAs: 'blob'` — the response isn't an envelope at all, so they check `response.ok` directly instead. `unwrap` is the place that tolerates the backend's two response shapes, collapsing both into a plain `T` or a thrown `ApiError`:

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

The two response shapes correspond to two layers of the backend:

- **Business envelope** — `Result<T>` (`{ code, msgKey, args, data }`), used on 2xx and on business-level failures like missing permission (403) or an invalid token (401): `code !== 0` throws an `ApiError` carrying the backend's numeric code and `msgKey`.
- **ProblemDetails** — ASP.NET's own error shape (`{ title, detail, ... }`, no `code` field), for cases the framework rejects before business code ever runs: model-validation failures (400), unhandled exceptions (500). These get wrapped into an `ApiError` built from the HTTP status plus `title`/`detail`.

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

How `translateError` turns `err` into that Chinese/English message is the subject of the "How error codes become text" section below.

## Pagination helpers: `pageParams` / `toPage`

Every list endpoint repeats the same two conversions, so they're factored out:

```ts
export const pageParams = (p: { page: number; pageSize: number }) => ({ Current: p.page, Size: p.pageSize })

export function toPage<T>(res: Parameters<typeof unwrap>[0]): { items: T[]; total: number } {
  const p = unwrap<PagedList<T>>(res)
  return { items: p.items, total: p.total }
}
```

- **Request side** — the frontend's `{ page, pageSize }` becomes the backend record's PascalCase `{ Current, Size }` query params (ASP.NET model binding is case-insensitive on names, but this repo's convention is Pascal). `...pageParams(p)` spreads into each endpoint's own strongly-typed query object, alongside its business filters.
- **Response side** — the backend's `PagedList<T>` (`{ current, size, total, items }`) becomes `{ items, total }`, exactly the shape ProTable's `fetcher` contract expects.

## How error codes become text

The `ApiError` that `unwrap` throws carries a numeric `code` and a `msgKey`. How those two become a Chinese/English message is the least obvious link in the whole frontend-backend contract.

The root of the convention: **the backend never sends localized text.** Every business error is a numeric `ErrorCode` (`backend/src/TenonAdmin.Core/ErrorCode.cs`), and each enum member is tagged with a `[MsgKey("...")]` dot-path:

```csharp
/// <summary>文件超过大小上限;args 可携带 maxSizeMb</summary>
[MsgKey("error.file.tooLarge")]
FileTooLarge = 44002,
```

The design is this way because translation should happen exactly once, and on the frontend: the backend hands over a code, the frontend looks up text by that code, the same error gets one sentence in each of the Chinese and English dictionaries, and the backend stays out of the language business entirely.

`ErrorCodeExtensions.GetMsgKey()` reflects once on first access (the result cached into a `FrozenDictionary`) and returns that key. **A code with no `[MsgKey]` falls back to `error.code.{numeric value}`** — an unmapped `44099` produces `error.code.44099`. That fallback exists so a missing attribute degrades to a still-locatable string instead of throwing.

The frontend's `error.*` i18n namespace mirrors these dot-paths one for one:

```ts
// src/locales/zh-CN.ts
error: {
  _fallback: '操作失败,请稍后重试',
  file: {
    tooLarge: '文件超出大小限制',
    // ...
  },
}
```

`src/utils/error.ts` is where the two sides actually meet:

```ts
export function translateError(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.msgKey && i18n.global.te(err.msgKey)) return t(err.msgKey)
    if (err.message) return err.message
  }
  if (err instanceof Error && err.message) return err.message
  return t('error._fallback')
}
```

`i18n.global.te(key)` just checks whether the key exists in the current locale — a purely local probe, no network call. So the full path for a `FileTooLarge` (44002) is:

1. The backend throws `AdminException(ErrorCode.FileTooLarge)`.
2. The response envelope carries `code: 44002`, `msgKey: "error.file.tooLarge"`, plus a non-localized `message` (a debug-only fallback).
3. `translateError` finds `i18n.global.te('error.file.tooLarge')` → `true` → renders `t('error.file.tooLarge')` → `"文件超出大小限制"` / `"File too large"`.

How error keys themselves get added, and how the namespaces are organized, is no different from ordinary copy keys and belongs to [Internationalization](/frontend/i18n); this page is only about the code-to-key half of the mapping.

::: warning Adding an error code is two halves of one change
Suppose you add a new backend error code — `[MsgKey("error.file.duplicateHash")] FileDuplicateHash = 44007` — but nobody adds `error.file.duplicateHash` to `zh-CN.ts` / `en-US.ts`. Here's what happens: `i18n.global.te(...)` returns `false`, and `translateError` falls through to `err.message`. But `err.message` defaults to the msgKey itself (`Result.Message = message ?? code.GetMsgKey()`), so what actually lands in the UI is the raw string `error.file.duplicateHash`, verbatim. **Adding a backend `ErrorCode` and adding the matching frontend `error.xxx.yyy` key are two halves of one change** — and this isn't only a runtime embarrassment: `ErrorCodeLocaleConsistencyTests` checks that every `[MsgKey]`'s leaf segment exists in both language packs, so a missing entry turns a **backend** test red — while the person debugging it is usually looking on the frontend.
:::

## Regenerating the contract

`src/api/schema.d.ts` isn't hand-written — it's generated from the `/openapi/v1.json` of a **running** backend instance. After changing a backend endpoint or DTO, regenerate it:

```bash
npm run gen:api
```

The key word is "running": `gen:api` uses openapi-typescript's live fetch, not an offline scan of the code. Before you run it the backend must be up (`dotnet run --project backend/samples/MinimalHost`, or `dev.bat` to start both halves at once), and `http://localhost:5100/openapi/v1.json` must be reachable — only then does `gen:api` have a contract to fetch. With the backend not started, that endpoint isn't reachable and the command gets nothing back.

::: warning
`schema.d.ts` is a generated artifact — don't hand-edit it; the next `gen:api` run overwrites it. To adjust types, change the backend endpoint/DTO and regenerate.
:::

How the envelope itself is assembled on the backend — authentication, `[RolePermission]`, data-scope filtering, and where the `Result<T>` this page's `unwrap` consumes gets wrapped on — is the subject of the [Request Pipeline](/backend/request-pipeline).
