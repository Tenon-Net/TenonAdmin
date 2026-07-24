# 对接后端：响应契约与错误码

后端的响应有两种形状，前端只想要一种结果：一个能用的值，或者一个能 catch 的错误。收拢这两者的是 `unwrap` 一个函数，它把每种响应都解成一个 `T`，或者抛出一个 `ApiError`。

请求怎么带着类型和令牌发出去，归 [请求层](/zh/frontend-react/request)；这里从响应回到手里那一刻接手。

## `unwrap` 与 `ApiError`

`web-react/src/api/index.ts` 里的 API 函数是手写的。`gen:api` 只生成 `schema.d.ts` 的类型，函数体自己写，绝大多数最后落在 `.then(r => unwrap<T>(r))`。这里的 `r` 是 `client.GET/POST(...)` 直接 resolve 出来的原始结果，形状固定是 `{ data, error, response }`，这是 openapi-fetch 的约定，不是 TenonAdmin 定的。分页端点落在 `toPage`，内部仍旧调 `unwrap`；只有文件下载不一样，它走 `parseAs: 'blob'`，响应根本不是信封，自己判 `response.ok` 就够。

`unwrap` 就是容忍两种响应形状的那个地方。它把两种情况都收成一个 `T`，或者一个抛出的 `ApiError`：

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

两种形状分别对应后端两层：

- **业务信封**：`Result<T>`，形状是 `{ code, msgKey, args, data }`。2xx 正常响应走它，缺权限（403）、令牌无效（401）这类业务级失败也走它。判据很简单：`code !== 0` 就抛一个 `ApiError`，带上后端的数字码和 `msgKey`。
- **ProblemDetails**：ASP.NET 框架自己的错误形状，长这样 `{ title, detail, ... }`，没有 `code` 字段。它对应业务代码根本没跑起来、就被框架拦下的情况，比如模型校验失败（400）、未处理异常（500）。这类被包成一个 `ApiError`，由 HTTP 状态码加 `title`/`detail` 拼出来。

React 版这里比 Vue 版多一道防线：响应体不是对象、或者信封里 `code` 不是数字，`unwrap` 直接抛 `Malformed API response`，而不是把一个形状不对的东西当 `data` 返回。契约错乱时早报错，好过让下游拿着 `undefined` 走更远。

`ApiError` 携带的信息够展示，也够程序化判断：

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

视图层这样接：

```tsx
try {
  await userApi.remove(id)
} catch (e) {
  message.error(translateError(e))
}
```

至于 `translateError` 怎么把 `e` 变成那句中文或英文，是下面「错误码怎么变成文案」一节的事。

## 分页帮手：`pageParams` / `toPage`

每个列表接口都要重复同样两处转换，抽出来：

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

- **请求方向**：前端的 `{ page, pageSize }` 变成后端 record 的 PascalCase `{ Current, Size }`。ASP.NET 绑定对名字大小写其实不敏感，但本仓库的约定是用 Pascal。`...pageParams(p)` 展进每个接口自己强类型的查询对象，和各自的业务过滤条件并排放。
- **响应方向**：后端的 `PagedList<T>`（`{ current, size, total, items }`）变成 `{ items, total }`。这正是 `DataTable`（隔离 pro-components 的那层）`request` 要的形状。`toPage` 顺手校验 `items` 是数组、`total` 是数字，不是就当 `Malformed paged API response` 抛掉。

`unwrap`、`ApiError`、`pageParams`、`toPage` 这四个一起导出，是留给消费者的接缝。消费者新建 `api/<域>.ts` 从这里 import，就不必把自己的端点塞进 `index.ts`。`index.ts` 是上游自留地，改一次就要在下次合并上游时解一次冲突。所以这四个导出即便站内没有别的调用方，也不能当未使用导出清掉。

## 错误码怎么变成文案

`unwrap` 抛出的 `ApiError` 带着一个数字 `code` 和一个 `msgKey`，把这两样落成一句中文或英文提示，是前后端约定里最不直观的一环。

约定的根子是一句话：**后端从不下发本地化文案**。每个业务错误都是一个数字 `ErrorCode`，定义在 `backend/src/TenonAdmin.Core/ErrorCode.cs`。每个枚举成员标一个 `[MsgKey("...")]` 点分路径：

```csharp
/// <summary>文件超过大小上限;args 可携带 maxSizeMb</summary>
[MsgKey("error.file.tooLarge")]
FileTooLarge = 44002,
```

为什么这么设计？后端要是直接吐一句翻译好的文案，它就得先知道这个用户切没切成英文，错误码和语言状态从此焊死在一起，谁都别想单独换。现在后端只给码，前端拿码查文案，同一个错误在中英文两套词典里各写一句，翻译因此只发生这一次、只发生在前端。

`GetMsgKey()` 首次访问时反射一次，把键取出来缓存进 `FrozenDictionary`。**没标 `[MsgKey]` 的码会回退成 `error.code.{数值}`**，一个没映射的 `44099` 产出的就是 `error.code.44099`。这个兜底的用处是漏标注时不抛异常，只退化成一个还能定位问题的字符串。

前端的 `error.*` 命名空间和这些点分路径逐一对应，落在 `web-react/src/locales/zh-CN.ts` 与 `en-US.ts`：

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

两边真正碰头的地方是 `web-react/src/utils/error.ts`：

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

这里的 `te` 不是 i18next 自带的 `i18n.exists()`，是 `web-react/src/locales/index.ts` 里手写的一格。差别只在**子树键**上，却会直接烧到用户脸上：后端 msgKey 万一恰好是 `error.auth` 这种指向一整棵子树的路径，`i18n.exists('error.auth')` 说 `true`，而 `t('error.auth')` 返回的是一句英文 debug 文本 `key 'error.auth' returned an object instead of string.`。用 `exists()` 就会把这句 debug 文本当文案弹给用户。`te` 的实现是 `i18n.exists(key) && typeof i18n.t(key, { returnObjects: true }) === 'string'`，对子树返回 `false`，于是退回后端原文。Vue 侧不必操心这一格，因为 vue-i18n 的 `te` 天生就是这个语义；React 这边是拿 i18next 拼出来的，所以要专门守。

一个 `FileTooLarge`（44002）的完整链路于是这样：

1. 后端抛 `AdminException(ErrorCode.FileTooLarge)`。
2. 响应信封携带 `code: 44002`、`msgKey: "error.file.tooLarge"`，外加一段非本地化的 `message`（仅作调试兜底）。
3. `translateError` 判断 `te('error.file.tooLarge')` → `true` → 渲染 `t('error.file.tooLarge')` → `"文件超出大小限制"` / `"File too large"`。

错误键本身怎么加、命名空间怎么组织，都和普通文案键没区别，那是 [国际化](/zh/frontend-react/i18n) 的事。消费者给自己的错误码补文案，也不该改 `zh-CN.ts`，而是往 `locales/ext/<locale>/error.ts` 放键，深合并进内置命名空间，同样零冲突。

::: warning 加错误码是一次改动的两半
假设后端新加一个 `[MsgKey("error.file.duplicateHash")] FileDuplicateHash = 44007`，但没人在 `zh-CN.ts` / `en-US.ts` 补上 `error.file.duplicateHash`。`te(...)` 返回 `false`，`translateError` 退到 `err.message`。而 `err.message` 默认就等于 msgKey 本身，因为后端 `Result.Message = message ?? code.GetMsgKey()`，`ApiError` 构造又把 `message ?? msgKey` 当消息。于是用户界面上直接弹出 `error.file.duplicateHash` 这串原始键。

**加一个后端 `ErrorCode`，和加对应的前端 `error.xxx.yyy` 键，是同一次改动的两半。**

这里和 Vue 侧有一处必须讲清的差别：后端的 `ErrorCodeLocaleConsistencyTests` 只扫 `web/src/locales`，**不覆盖 `web-react/`**。也就是说漏配一个键，React 模板这边没有任何测试会变红，唯一的症状是运行期界面上弹出裸 msgKey。这条纪律在 React 这侧因此更吃紧：没有安全网替你兜。
:::

## 重新生成契约

`web-react/src/api/schema.d.ts` 不是手写的，是从一个**正在运行的**后端实例抓 `/openapi/v1.json` 生成出来的类型。改了后端接口或 DTO，重新生成一遍：

```bash
npm run gen:api
```

关键在「正在运行」四个字。`gen:api` 走 openapi-typescript 在线抓取，不是离线扫代码，所以跑之前后端得先起着，`http://localhost:5100/openapi/v1.json` 能访问，命令才有契约可抓。起后端用 `dotnet run --project backend/samples/MinimalHost`，或者 `dev.bat` 一键起前后端。

::: warning
`schema.d.ts` 是生成产物，不要手改。改了它，下次一跑 `gen:api` 就被覆盖。要调整类型，去改后端的接口或 DTO，再重新生成。
:::

信封本身在后端怎么拼出来、`unwrap` 消费的 `Result<T>` 到底在哪一步套上，归 [请求管线](/zh/backend/request-pipeline) 那页。`web-react/` 和 Vue 模板对接的是同一个后端、同一套 `ErrorCode`，所以这层契约两边同构；两个模板为什么各留一份而不抽公共层，见 [前端模板](/zh/guide/frontend-templates)。
