# HTTP 请求层

所有接口请求都经过一个 openapi-fetch 客户端和三个中间件，方法签名由后端 OpenAPI 契约生成。认证中间件附加令牌与 CSRF；刷新中间件在 401 后换发令牌并重放原请求；再认证中间件处理 403 + 40024，在敏感操作前补一次身份确认。业务页面只调用接口，不重复实现这三段安全流程。

> 前置：先完成[入门教程的接口步骤](/zh/frontend/getting-started#_4-再接上真实接口和类型)。

## 先跟踪一次岗位查询

打开浏览器网络面板，再进入「岗位预览」。选中 `/api/v1/sys/position/page` 请求，依次确认三件事：查询参数里有 `Current=1` 和 `Size=20`，请求头带当前 access token，响应体外层有数字 `code`。点击页面的「刷新」，应只多出一次同路径请求。

视图代码里没有令牌或 URL 拼接，网络面板里却已经出现完整请求。中间这段差距正是 `positionApi → client → middleware → fetch`，下面从生成契约开始逐层展开。

## 全景

```text
后端 OpenAPI (/openapi/v1.json)
  │  npm run gen:api
  ▼
src/api/schema.d.ts        生成的类型(paths,禁止手改)
  │
  ▼
src/api/client.ts          带类型的客户端 + 认证/刷新/再认证中间件
  │
  ▼
src/api/index.ts           按领域分组的 API 函数,统一形态:client.X(...).then(r => unwrap<T>(r))
  │
  ▼
views                       catch ApiError,经 translateError(err) 展示
```

图里下面两格，是响应回来之后的事。`src/api/index.ts` 的 `unwrap` 把后端两种响应形状收拢成一个结果。视图层的 `translateError` 把错误码变成展示文案。要是你找的是这两段，去[对接后端响应](/zh/frontend/api-contract)。这里只讲请求怎么带着类型和令牌发出去，到 `client.ts` 为止。

## 重新生成契约：`gen:api`

```bash
npm run gen:api   # openapi-typescript http://localhost:5100/openapi/v1.json -o src/api/schema.d.ts
```

- 后端必须先跑起来。脚本要向一个真实运行中的服务器拉 `/openapi/v1.json`，默认地址是 `http://localhost:5100`。后端跑在别处时，看 `web/vite.config.ts` 里 `TENON_API_TARGET` 对应的 dev 代理目标。
- `src/api/schema.d.ts` 是**生成产物**，禁止手改。改了后端的接口或 DTO，重新生成一遍就行。手改的东西，下次生成会被无声覆盖。
- `src/api/client.ts` 的 `createClient<paths>()` 拿这份文件当类型源。所以每一次 `client.GET/POST/PUT/DELETE` 调用，从路径参数、查询参数、请求体到响应形状，全链路的类型都是后端真实契约推出来的。

<a id='类型化客户端与两个中间件'></a>
## 类型化客户端与三个中间件

```ts
const baseUrl = import.meta.env.VITE_API_BASE ?? ''
export const client = createClient<paths>({ baseUrl, credentials: 'include' })
// 刷新专用客户端:不挂任何中间件,刷新请求自己的 401 就没有递归的入口。
const bare = createClient<paths>({ baseUrl, credentials: 'include' })
```

`baseUrl` 默认为空。schema 的 path 键本身已经带了 `/api/v1`，`/api` 走同源。开发时 Vite 把它代理到后端，生产时靠反代或后端自托管。只有前端和 API 真的跨域，才需要设 `VITE_API_BASE`。

`credentials: 'include'` 让 Cookie 会话在同源和允许凭据的跨源部署中都能携带 HttpOnly refresh Cookie。三个中间件只挂在 `client` 上，`bare` 专门执行刷新请求。

### 认证中间件

```ts
const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = useUserStore().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    if (isMutating(request.method)) attachCsrf(request.headers)
    return request
  },
}
```

令牌在请求发出时读取，因此能拿到刚刷新出的最新值。`POST`、`PUT`、`PATCH`、`DELETE` 等状态变更请求还会从可读的 `tenon_csrf` Cookie 取值，写入 `X-Tenon-CSRF`。refresh Cookie 本身是 HttpOnly，JavaScript 读不到；浏览器通过 `credentials` 自动携带它。

### 401 刷新中间件，以及为什么重放需要一份克隆

这是 `client.ts` 里最不直观的一段。问题出在 `Request` 的 body 上，它是个流，只能读一次。一个 POST/PUT 请求被判 401 之后，流程要先刷新令牌，再拿同一个请求重放。可等响应回来的时候，原始请求的 body 早就被 `fetch` 读掉了，原样重放会把 body 发丢。

解法是在请求真正发出**之前**、body 流还没被碰过的时候，先克隆一份：

```ts
const replayable = new WeakMap<Request, Request>()

const refreshMiddleware: Middleware = {
  onRequest({ request }) {
    // 只有带 body 的写请求才需要留一份可重放副本 —— GET/HEAD 没有 body 可丢。
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

`Request.clone()` 把底层的 body 流一分为二，两份各自独立可读。原始请求照常发出去，没被动过的那份克隆存进一个 `WeakMap`，键就是原始 `Request` 实例。openapi-fetch 会把这同一个实例一路带到 `onResponse`。请求结束后，这个 `WeakMap` 条目自动回收，不用手动清理。

遇到 401，依次发生：

1. **跳过刷新和登录接口自身**：`/auth/refresh` 或 `/auth/login` 返回的 401 是真实的凭证失败，不是令牌过期。把它也塞进刷新流程，会死循环。
2. **`refreshOnce()`**：并发合流。同一时刻要是有好几个请求一起 401，也只发一次 `/auth/refresh`，让大家都等同一个 promise:
   ```ts
   let refreshing: Promise<boolean> | null = null
   function refreshOnce(): Promise<boolean> {
     refreshing ??= doRefresh().finally(() => { refreshing = null })
     return refreshing
   }
   ```
3. **刷新失败**（body 模式没有 refreshToken、网络错误、`code` 非零或没有 `data`）：清空用户、授权和动态路由状态，再跳转 `/login`。Cookie 模式的 refreshToken 在 HttpOnly Cookie 中，本地 store 为空是正常情况。
4. **刷新成功**：用发出前存的克隆副本重建请求，补上刚刷新出来的新令牌，再用**裸 `fetch()`** 重放。GET/HEAD 本来就没克隆，直接用原始请求。这里特意不再走一次 `client.GET/POST(...)`。再走 `client`，中间件链会在这次重放时再次执行。万一新令牌也被拒，又是一次 401，就会递归进下一轮刷新。

### 为什么 `doRefresh` 用 `bare` 而不是 `client`

```ts
async function doRefresh(): Promise<boolean> {
  const user = useUserStore()
  if (!user.cookieSession && !user.refreshToken) return false
  const headers: Record<string, string> = {}
  const csrf = readCookie(CSRF_COOKIE)
  if (csrf) headers[CSRF_HEADER] = csrf
  const { data, error } = await bare.POST('/api/v1/auth/refresh', {
    body: user.cookieSession ? { refreshToken: '' } : { refreshToken: user.refreshToken },
    headers,
  })
  const env = data as { code?: number; data?: unknown } | undefined
  if (error || !env || env.code !== 0 || !env.data) return false
  user.setSession(env.data as Parameters<typeof user.setSession>[0])
  return true
}
```

`bare` 是用同一份 schema 建的第二个 `openapi-fetch` 客户端，但**不挂任何中间件**。刷新请求走 `bare`，因此不会递归。即使 refreshToken 也已过期、刷新接口返回 401，这个响应也不会进入 `refreshMiddleware.onResponse`。`onResponse` 中跳过 `/auth/refresh`、`/auth/login` 的 URL 判断是第二道保护，同时覆盖经 `client` 调用登录失败的情况。避免递归的根本原因，是刷新请求不在 `client` 的中间件链上。

### 403 再认证中间件

部分敏感接口会返回 403 和业务码 40024，要求用户再次证明身份。`reauthMiddleware` 只拦截这一种响应，调用 `requestReauth()` 打开再认证流程，成功后用请求副本重放一次。重放请求会带 `X-Tenon-Reauth-Retry: 1`，防止服务端再次返回 40024 时形成循环；取消或认证失败则把原响应交回调用方。它复用刷新中间件预先保存的请求副本，因此带 body 的敏感操作不会在重试时丢数据。

## 开发代理与 CORS

类型化客户端（`src/api/client.ts`）默认从浏览器同源访问 `/api`：`client` 的 `baseUrl` 为空，请求使用相对 URL，不做跨域处理。`gen:api` 由 Node 直接请求固定的 `http://localhost:5100/openapi/v1.json`，不经过浏览器或 dev proxy，因此不受 CORS 约束。后端不在 5100 时，需要修改 `package.json` 中的生成命令，`TENON_API_TARGET` 对它无效。本地开发中，后端位于 `:5100`，Vue dev server 默认位于 `:5175`，两者由 Vite 代理连接；实际地址以 Vite 终端输出为准。

补这道缝的就是 `vite.config.ts` 里的 dev 代理：

```ts
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

server: {
  port: 5175,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
  },
},
```

它把 `:5175` 上的 `/api/*`、`/openapi/*` 请求转发给后端。浏览器始终只看到 Vue dev server 这一个源，因此不存在跨域。目标地址默认是 `http://localhost:5100`。后端跑在别处时，在启动 Vite 前设置 `TENON_API_TARGET`。

没有这层代理会怎样？类型化客户端的请求、`gen:api` 的 schema 拉取，都会直接打到后端的源上。后端 CORS 默认 deny-all，响应还没传到 `unwrap` 或 `openapi-typescript`，就被浏览器（或者 `gen:api` 的 fetch）拒了。是这层代理，让请求层「同源」这个前提在本地成立。

::: tip 生产环境没有这层代理
`npm run dev` 的代理只在开发期存在。生产构建出的 `web/dist` 是纯静态文件，请求怎么到后端，要在部署时自己解决。后端顺带托管前端产物，或者 nginx/Caddy 反代，都是同源，不用配 CORS。只有前端和后端真跨源，比如前端上 CDN、后端独立域名，才需要动 `TenonAdmin:Api:Cors:AllowedOrigins`，方案见[部署路线 C：真跨源](/zh/guide/deployment/route-c)。
:::

## 在浏览器里确认请求链路

登录后打开浏览器网络面板，任选一个受保护的查询和一个写操作。查询请求应带最新的 `Authorization`；Cookie 会话下的写操作还应带 `X-Tenon-CSRF`。让 access token 过期后再发一次请求，网络面板可以看到一次刷新，原请求随后重放并成功；并发触发多个请求时也只应出现一次刷新。刷新失败则应清空会话并回到登录页，不能在后台继续重复 401。

完整代理配置与联调别名见[项目结构与启动](/zh/frontend/structure)。
