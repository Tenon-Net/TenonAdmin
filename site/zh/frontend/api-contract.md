# 对接后端:响应契约与错误码

生成出来的每个 API 函数,最后都要把后端的响应变成一个能用的值,或者一个能展示的错误。这页讲这套收拢怎么做:`unwrap` 怎么容忍两种响应形状、分页怎么转,以及一个数字错误码怎么最终落成用户看到的那句提示。

## `unwrap` 与 `ApiError`

每个生成出来的 API 函数最后都落在 `.then(r => unwrap<T>(r))` 上。`unwrap` 是唯一容忍后端两种响应形状的地方,把两种情况都收拢成一个 `T` 或者一个抛出的 `ApiError`:

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

两种响应形状,分别对应后端的两层:

- **业务信封** —— `Result<T>`(`{ code, msgKey, args, data }`),2xx 和缺权限(403)、令牌无效(401)这类业务级失败都走这个形状:`code !== 0` 就抛出带后端数字码和 `msgKey` 的 `ApiError`。
- **ProblemDetails** —— ASP.NET 框架自己的错误形状(`{ title, detail, ... }`,没有 `code` 字段),对应业务代码根本没跑起来就被框架拦下的情况:模型校验失败(400)、未处理异常(500)。这类会被包装成一个由 HTTP 状态码 + `title`/`detail` 拼出来的 `ApiError`。

`ApiError` 携带的信息够展示也够程序化判断:

```ts
export class ApiError extends Error {
  code: number
  msgKey?: string
  args?: Record<string, unknown>
}
```

视图层这样接:

```ts
try {
  await userApi.remove(id)
} catch (err) {
  message.error(translateError(err))
}
```

`translateError` 怎么把 `err` 变成那句中文/英文提示,是下面「错误码怎么变成文案」一节的事。

## 分页帮手:`pageParams` / `toPage`

每个列表接口都要重复同样两处转换,所以抽了出来:

```ts
const pageParams = (p: { page: number; pageSize: number }) => ({ Current: p.page, Size: p.pageSize })

function toPage<T>(res: Parameters<typeof unwrap>[0]): { items: T[]; total: number } {
  const p = unwrap<PagedList<T>>(res)
  return { items: p.items, total: p.total }
}
```

- **请求方向** —— 前端的 `{ page, pageSize }` 变成后端 record 的 PascalCase `{ Current, Size }` 查询参(ASP.NET 绑定对名字大小写不敏感,但本仓库的约定是用 Pascal)。`...pageParams(p)` 展开进每个接口自己强类型的查询对象里,和各自的业务过滤条件并列。
- **响应方向** —— 后端的 `PagedList<T>`(`{ current, size, total, items }`)变成 `{ items, total }`,正是 ProTable `fetcher` 契约要的形状。

## 错误码怎么变成文案

`unwrap` 抛出的 `ApiError` 带着一个数字 `code` 和一个 `msgKey`。这两样怎么落成一句中文/英文提示,是前后端约定里最不直观的一环。

约定的根:**后端从不下发本地化文案**。每个业务错误都是一个数字 `ErrorCode`(`backend/src/TenonAdmin.Core/ErrorCode.cs`),每个枚举成员标一个 `[MsgKey("...")]` 点分路径:

```csharp
/// <summary>文件超过大小上限;args 可携带 maxSizeMb</summary>
[MsgKey("error.file.tooLarge")]
FileTooLarge = 44002,
```

这么设计,是因为翻译只该发生一次、且发生在前端:后端给码,前端按码查文案,同一个错误在中英文两套词典里各写一句,后端不掺和语言的事。

`ErrorCodeExtensions.GetMsgKey()` 首次访问时反射一次(结果缓存进 `FrozenDictionary`),取出这个键。**没标 `[MsgKey]` 的码会回退成 `error.code.{数值}`**——一个没映射的 `44099` 产出 `error.code.44099`。这个兜底的用处是:漏标注不会抛异常,而是退化成一个还能定位问题的字符串。

前端的 `error.*` i18n 命名空间和这些点分路径逐一对应:

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

`src/utils/error.ts` 是两边真正碰头的地方:

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

`i18n.global.te(key)` 只检查当前语言里存不存在这个键,是纯本地判断,不发网络请求。所以一个 `FileTooLarge`(44002)的完整链路是:

1. 后端抛 `AdminException(ErrorCode.FileTooLarge)`。
2. 响应信封携带 `code: 44002`、`msgKey: "error.file.tooLarge"`,外加一段非本地化的 `message`(仅作调试兜底)。
3. `translateError` 判断 `i18n.global.te('error.file.tooLarge')` → `true` → 渲染 `t('error.file.tooLarge')` → `"文件超出大小限制"` / `"File too large"`。

错误键本身怎么加、命名空间怎么组织,和普通文案键没区别,归 [国际化](/zh/frontend/i18n) 那页管;这页只管码到键这一段映射。

::: warning 加错误码是一次改动的两半
新加一个后端错误码——`[MsgKey("error.file.duplicateHash")] FileDuplicateHash = 44007`——但没人在 `zh-CN.ts` / `en-US.ts` 补上 `error.file.duplicateHash`,会发生什么:`i18n.global.te(...)` 返回 `false`,`translateError` 退到 `err.message`(后端那段非本地化的调试文案),连它也是空就再退到 `error._fallback`。不崩溃,但用户看到的是一句通用或语气不对的提示,而不是本该显示的那句。**加一个后端 `ErrorCode` 和加对应的前端 `error.xxx.yyy` 键,是同一次改动的两半**——只做前一半的 PR 不完整。
:::

## 重新生成契约

`src/api/schema.d.ts` 不是手写的,是从一个**正在运行的**后端实例的 `/openapi/v1.json` 抓下来生成的类型。改动后端接口或 DTO 后,重新生成一遍:

```bash
npm run gen:api
```

关键在「正在运行」四个字:`gen:api` 走的是 openapi-typescript 在线抓取,不是离线扫代码。跑之前后端得先起着(`dotnet run --project backend/samples/MinimalHost`,或用 `dev.bat` 一键起前后端),确认 `http://localhost:5100/openapi/v1.json` 能访问,`gen:api` 才有契约可抓——后端没启动,这个端点访问不到,命令就拿不到数据。

::: warning
`schema.d.ts` 是生成产物,不要手改——改了下次一 `gen:api` 就被覆盖。要调整类型,改后端的接口/DTO 再重新生成。
:::

信封本身在后端怎么被拼出来——认证、`[RolePermission]`、数据范围过滤,以及这页 `unwrap` 消费的 `Result<T>` 是在哪一步套上的——是 [请求管线](/zh/backend/request-pipeline) 的事。
