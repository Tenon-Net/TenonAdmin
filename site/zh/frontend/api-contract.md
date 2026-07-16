# 对接后端响应

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

## 错误文案:`translateError`

`ApiError.msgKey` 是接到展示文案的那根线 —— `translateError`(`src/utils/error.ts`)拿它去查 i18n 词典,查不到退回 `.message`,再退回一个通用兜底文案。`msgKey` 的约定和后端错误码怎么映射成翻译文案,见 [i18n](/zh/frontend/i18n)。


## 接下来看什么

- [i18n](/zh/frontend/i18n) —— `msgKey` 怎么变成本地化文案
- [权限](/zh/frontend/permission) —— `v-auth` 指令与权限码
- [请求管线](/zh/backend/request-pipeline) —— 后端这一侧的对应篇:认证、`[RolePermission]`、数据范围,以及本页 `unwrap` 消费的 `Result<T>` 信封
