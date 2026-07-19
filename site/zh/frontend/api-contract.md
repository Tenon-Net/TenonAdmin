# 对接后端：响应契约与错误码

生成出来的每个 API 函数，最后都得把后端的响应收成一样东西：要么一个能用的值，要么一个能展示的错误。这件事全站只有一个地方在做。它得同时应付两种响应形状：业务信封带着一个数字错误码，框架的 ProblemDetails 却连码都没有。

## `unwrap` 与 `ApiError`

`src/api/index.ts` 里的 API 函数是手写的。`gen:api` 只生成 `schema.d.ts` 的类型，函数得自己写。它们绝大多数最后落在 `.then(r => unwrap<T>(r))` 上。这里的 `r` 就是 `client.GET/POST(...)` 直接 resolve 出来的原始结果，形状固定是 `{ data, error, response }`，这是 openapi-fetch 自己的约定，不是 TenonAdmin 发明的。分页端点落在 `toPage`，但它内部仍旧调 `unwrap`。只有文件下载不一样，它走 `parseAs: 'blob'`，响应根本不是信封，自己判 `response.ok` 就行。`unwrap` 就是容忍后端两种响应形状的那个地方。它把两种情况都收拢成一个 `T`，或者一个抛出的 `ApiError`：

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

两种响应形状，分别对应后端的两层：

- **业务信封**：`Result<T>`，形状是 `{ code, msgKey, args, data }`。2xx 正常响应走它，缺权限（403）、令牌无效（401）这类业务级失败也走它。判据很简单：`code !== 0` 就抛出一个 `ApiError`，带上后端的数字码和 `msgKey`。
- **ProblemDetails**：ASP.NET 框架自己的错误形状，长这样 `{ title, detail, ... }`，没有 `code` 字段。它对应的是业务代码根本没跑起来、就被框架拦下的情况，比如模型校验失败（400）、未处理异常（500）。这类会被包装成一个 `ApiError`，由 HTTP 状态码加 `title`/`detail` 拼出来。

`ApiError` 携带的信息够展示也够程序化判断：

```ts
export class ApiError extends Error {
  code: number
  msgKey?: string
  args?: Record<string, unknown>
}
```

视图层这样接：

```ts
try {
  await userApi.remove(id)
} catch (err) {
  message.error(translateError(err))
}
```

至于 `translateError` 怎么把 `err` 变成那句中文或英文提示，是下面「错误码怎么变成文案」一节的事。

## 分页帮手：`pageParams` / `toPage`

每个列表接口都要重复同样两处转换，干脆抽出来：

```ts
export const pageParams = (p: { page: number; pageSize: number }) => ({ Current: p.page, Size: p.pageSize })

export function toPage<T>(res: Parameters<typeof unwrap>[0]): { items: T[]; total: number } {
  const p = unwrap<PagedList<T>>(res)
  return { items: p.items, total: p.total }
}
```

- **请求方向**：前端的 `{ page, pageSize }` 变成后端 record 的 PascalCase `{ Current, Size }` 查询参。ASP.NET 绑定对名字大小写其实不敏感，但本仓库的约定是用 Pascal。`...pageParams(p)` 展开进每个接口自己强类型的查询对象里，和各自的业务过滤条件并排放。
- **响应方向**：后端的 `PagedList<T>`（`{ current, size, total, items }`）变成 `{ items, total }`。这正是 ProTable `fetcher` 契约要的形状。

## 错误码怎么变成文案

`unwrap` 抛出的 `ApiError` 带着一个数字 `code` 和一个 `msgKey`。这两样怎么落成一句中文或英文提示？这是前后端约定里最不直观的一环。

约定的根子是一句话：**后端从不下发本地化文案**。每个业务错误都是一个数字 `ErrorCode`，定义在 `backend/src/TenonAdmin.Core/ErrorCode.cs`。每个枚举成员标一个 `[MsgKey("...")]` 点分路径：

```csharp
/// <summary>文件超过大小上限;args 可携带 maxSizeMb</summary>
[MsgKey("error.file.tooLarge")]
FileTooLarge = 44002,
```

为什么这么设计？因为翻译只该发生一次，而且只发生在前端。后端只给码，前端拿码查文案。同一个错误，中英文两套词典里各写一句，后端不掺和语言的事。

`ErrorCodeExtensions.GetMsgKey()` 首次访问时反射一次，把这个键取出来，结果缓存进 `FrozenDictionary`。**没标 `[MsgKey]` 的码会回退成 `error.code.{数值}`**。比如一个没映射的 `44099`，产出的就是 `error.code.44099`。这个兜底有什么用？漏标注时不会抛异常，只会退化成一个还能定位问题的字符串。

前端的 `error.*` i18n 命名空间和这些点分路径逐一对应：

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

`src/utils/error.ts` 是两边真正碰头的地方：

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

`i18n.global.te(key)` 只检查当前语言里存不存在这个键。它是纯本地判断，不发网络请求。所以一个 `FileTooLarge`（44002）的完整链路是这样：

1. 后端抛 `AdminException(ErrorCode.FileTooLarge)`。
2. 响应信封携带 `code: 44002`、`msgKey: "error.file.tooLarge"`，外加一段非本地化的 `message`（仅作调试兜底）。
3. `translateError` 判断 `i18n.global.te('error.file.tooLarge')` → `true` → 渲染 `t('error.file.tooLarge')` → `"文件超出大小限制"` / `"File too large"`。

错误键本身怎么加、命名空间怎么组织，都和普通文案键没区别。那是 [国际化](/zh/frontend/i18n) 的事。

::: warning 加错误码是一次改动的两半
假设后端新加一个错误码 `[MsgKey("error.file.duplicateHash")] FileDuplicateHash = 44007`，但没人在 `zh-CN.ts` / `en-US.ts` 补上 `error.file.duplicateHash`。会发生什么？`i18n.global.te(...)` 返回 `false`，`translateError` 退到 `err.message`。而 `err.message` 默认就等于 msgKey 本身，因为 `Result.Message = message ?? code.GetMsgKey()`。于是用户界面上直接弹出的，是 `error.file.duplicateHash` 这串原始键。**加一个后端 `ErrorCode`，和加对应的前端 `error.xxx.yyy` 键，是同一次改动的两半**。这还不只是运行期难看。`ErrorCodeLocaleConsistencyTests` 会校验每个 `[MsgKey]` 的叶子段在两份语言包里都存在。漏配会让**后端测试**直接变红，可人往往跑去前端找原因。
:::

## 重新生成契约

`src/api/schema.d.ts` 不是手写的。它是从一个**正在运行的**后端实例，抓 `/openapi/v1.json` 生成出来的类型。改了后端接口或 DTO，重新生成一遍：

```bash
npm run gen:api
```

关键就在「正在运行」四个字。`gen:api` 走的是 openapi-typescript 在线抓取，不是离线扫代码。所以跑之前后端得先起着，确认 `http://localhost:5100/openapi/v1.json` 能访问，`gen:api` 才有契约可抓。起后端可以用 `dotnet run --project backend/samples/MinimalHost`，或者 `dev.bat` 一键起前后端。后端没启动，这个端点就访问不到，命令也拿不到数据。

::: warning
`schema.d.ts` 是生成产物，不要手改。改了它，下次一跑 `gen:api` 就被覆盖。要调整类型，去改后端的接口或 DTO，再重新生成。
:::

信封本身在后端怎么拼出来，归 [请求管线](/zh/backend/request-pipeline) 那页：认证、`[RolePermission]`、数据范围过滤，还有这页 `unwrap` 消费的 `Result<T>` 到底在哪一步套上。
