# HTTP 请求层

链路只有这么长：一个 openapi-fetch 客户端加两个中间件，客户端的类型全由后端 OpenAPI 契约推出来。第一个中间件只干一件事，请求发出前挂上令牌。难的是第二个：令牌过期必须对业务代码完全隐形，401 之后自动刷新、重放，调用方连一次报错都看不到。

## 全景

```text
后端 OpenAPI (/openapi/v1.json)
  │  npm run gen:api
  ▼
src/api/schema.d.ts        生成的类型(paths,禁止手改)
  │
  ▼
src/api/client.ts          带类型的 openapi-fetch 客户端 + 认证/刷新中间件
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

## 类型化客户端与两个中间件

```ts
const baseUrl = import.meta.env.VITE_API_BASE ?? ''
export const client = createClient<paths>({ baseUrl })
// 刷新专用客户端:不挂任何中间件,刷新请求自己的 401 就没有递归的入口。
const bare = createClient<paths>({ baseUrl })
```

`baseUrl` 默认为空。schema 的 path 键本身已经带了 `/api/v1`，`/api` 走同源。开发时 Vite 把它代理到后端，生产时靠反代或后端自托管。只有前端和 API 真的跨域，才需要设 `VITE_API_BASE`。

两个中间件挂在 `client` 上（不挂在 `bare` 上，原因见下文）：

### 认证中间件

```ts
const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = useUserStore().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    return request
  },
}
```

令牌是请求发出时才去 store 读的，不是模块加载时读一次就存住。所以每次都能拿到最新的令牌，包括刚刚才刷新出来的那个。

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
3. **刷新失败**（没有 refreshToken、网络错误、`code` 非零，或没有 `data`）：清空会话，跳转 `/login`。这里 router 用的是惰性 import，免得和 `client.ts` 形成静态循环依赖。
4. **刷新成功**：用发出前存的克隆副本重建请求，补上刚刷新出来的新令牌，再用**裸 `fetch()`** 重放。GET/HEAD 本来就没克隆，直接用原始请求。这里特意不再走一次 `client.GET/POST(...)`。再走 `client`，两个中间件会在这次重放上又跑一遍。万一新令牌也被拒，又是一次 401，就会递归进下一轮刷新。

### 为什么 `doRefresh` 用 `bare` 而不是 `client`

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

`bare` 是用同一份 schema 建的第二个 `openapi-fetch` 客户端，但**不挂任何中间件**。刷新请求走 `bare`，好处是它不会递归。就算刷新本身失败，比如 refreshToken 也过期了、接口照样答 401，这个 401 也进不了 `refreshMiddleware.onResponse`，因为 `bare` 上根本没有中间件链可以递归。`onResponse` 里那道跳过 `/auth/refresh`/`/auth/login` 的 URL 判断只是第二道保险，顺带覆盖经 `client` 调用登录失败的情况。刷新请求自身能防住递归，根子上靠的是它压根不在 `client` 的中间件链上。

## 开发代理与 CORS

类型化客户端（`src/api/client.ts`）默认浏览器是同源访问 `/api` 的：`client` 的 `baseUrl` 默认为空，请求走的是一个看起来相对的 URL，没做任何跨域处理。`gen:api` 不一样，它压根不经过浏览器：命令由 Node 直接发给写死的 `http://localhost:5100/openapi/v1.json`，不走 dev proxy，自然也就没有 CORS 这回事。后端不在 5100，就得改 `package.json` 里那行脚本，`TENON_API_TARGET` 对它无效。本地开发时，后端跑在 `:5100`，dev server 跑在 `:5173`，端口不一样。总得有个东西把这道缝补上，两边才能对得上。

补这道缝的就是 `vite.config.ts` 里的 dev 代理：

```ts
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

server: {
  port: 5173,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
  },
},
```

它把 `:5173` 上的 `/api/*`、`/openapi/*` 请求转发给后端。浏览器自始至终只看到一个源，也就是 `:5173`，自然不存在跨域问题。目标地址默认是 `http://localhost:5100`。后端跑在别处时，启动 Vite 前设一下 `TENON_API_TARGET` 就行。

没有这层代理会怎样？类型化客户端的请求、`gen:api` 的 schema 拉取，都会直接打到后端的源上。后端 CORS 默认 deny-all，响应还没传到 `unwrap` 或 `openapi-typescript`，就被浏览器（或者 `gen:api` 的 fetch）拒了。是这层代理，让请求层「同源」这个前提在本地成立。

::: tip 生产环境没有这层代理
`npm run dev` 的代理只在开发期存在。生产构建出的 `web/dist` 是纯静态文件，请求怎么到后端，要在部署时自己解决。后端顺带托管前端产物，或者 nginx/Caddy 反代，都是同源，不用配 CORS。只有前端和后端真跨源，比如前端上 CDN、后端独立域名，才需要动 `TenonAdmin:Api:Cors:AllowedOrigins`，方案见[部署路线 C：真跨源](/zh/guide/deployment/route-c)。
:::

完整代理配置与联调别名见[项目结构与启动](/zh/frontend/structure)。
