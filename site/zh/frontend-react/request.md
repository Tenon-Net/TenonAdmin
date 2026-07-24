# HTTP 请求层

请求层就两样东西：一个 openapi-fetch 客户端，加它上面的两个中间件。客户端方法的签名全由后端 OpenAPI 契约推出，从路径到响应形状都带类型。认证中间件只在发请求前挂令牌；难的是第二个：令牌过期对业务代码全隐形，401 后自动刷新重放，调用方看不到一次报错。

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
src/api/index.ts           按领域分组的 API 函数,统一形态:client.X(...).then((r) => unwrap<T>(r))
  │
  ▼
views                       catch ApiError,经 translateError 展示
```

图里下面两格是响应回来之后的事。`src/api/index.ts` 每个 API 函数都是 `client.X(...).then((r) => unwrap<T>(r))` 这一个形态，`unwrap` 把后端信封收拢成结果或抛 `ApiError`；视图层 catch 到再翻成展示文案。这两段属于[对接后端响应](/zh/frontend-react/api-contract)，这里只讲请求怎么带着类型和令牌发出去，到 `client.ts` 为止。

## 重新生成契约：`gen:api`

```bash
npm run gen:api   # openapi-typescript http://localhost:5100/openapi/v1.json -o src/api/schema.d.ts
```

- 后端必须先跑起来。脚本向一个真实运行的服务器拉 `/openapi/v1.json`，地址写死在 `package.json` 的这行脚本里，默认 `http://localhost:5100`。
- `src/api/schema.d.ts` 是**生成产物**，别手改。后端的接口或 DTO 变了，重新跑一遍就行；手改的东西下次生成会被无声覆盖。
- `client` 的 `createClient<paths>()` 拿这份文件当类型源。所以每一次 `client.GET/POST/PUT/DELETE` 调用，从路径参数、查询参数、请求体到响应形状，全链路类型都是后端真实契约推出来的。

## 类型化客户端与两个中间件

```ts
const baseUrl = import.meta.env.VITE_API_BASE ?? ''
const rawTransport = globalThis.fetch
const client = createClient<paths>({ baseUrl, fetch: rawTransport })
// 刷新专用客户端:不挂任何中间件,刷新请求自己的 401 就没有递归的入口。
const bare = createClient<paths>({ baseUrl, fetch: rawTransport })
```

`baseUrl` 默认为空。schema 的 path 键本身带了 `/api/v1`，`/api` 走同源；开发时 Vite 把它代理到后端，生产靠反代或后端自托管。只有前端和 API 真跨域，才需要设 `VITE_API_BASE`。

`rawTransport` 把原生 `globalThis.fetch` 抓在手里，两个客户端都拿它当底层传输。重放请求时也直接调 `rawTransport`，不再走 `client`，这样两个中间件不会在重放上重跑一遍。

两个中间件只挂在 `client` 上，不挂 `bare`（原因见下文），挂载顺序先 `authMiddleware` 后 `refreshMiddleware`。

### 认证中间件

```ts
const authMiddleware: Middleware = {
  onRequest({ request }) {
    const token = useUserStore.getState().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    return request
  },
}
```

令牌是请求发出时才去 store 读的，不是模块加载时读一次存住，所以每次都拿到最新令牌，包括刚刷新出来那个。这里取值走 `useUserStore.getState()`，不用 store 的 hook 形态：中间件跑在 React 渲染之外，hook 调不了，zustand 的 `getState()` 正好能在组件外同步取当前值。

### 401 刷新中间件，以及为什么重放需要一份克隆

`client.ts` 里最不直观的一段。问题出在 `Request` 的 body 上，它是个流，只能读一次。一个 POST/PUT 请求被判 401 后，流程要先刷新令牌、再拿同一个请求重放；可等响应回来时，原始请求的 body 早被 `fetch` 读掉了，原样重放会把 body 发丢。

解法是在请求真正发出**之前**、body 流还没被碰过时，先克隆一份：

```ts
const replayable = new WeakMap<Request, Request>()

const refreshMiddleware: Middleware = {
  onRequest({ request }) {
    // 只有带 body 的写请求才需要留一份可重放副本;GET/HEAD 没有 body 可丢。
    if (request.method !== 'GET' && request.method !== 'HEAD') {
      replayable.set(request, request.clone())
    }
    return request
  },
  async onResponse({ request, response }) {
    if (response.status !== 401) return response
    if (request.url.includes('/api/v1/auth/refresh') || request.url.includes('/api/v1/auth/login')) {
      return response
    }

    if (!(await refreshOnce())) {
      useUserStore.getState().clear()
      gotoLogin()
      return response
    }

    const base = replayable.get(request) ?? request
    const retry = new Request(base, { headers: new Headers(base.headers) })
    retry.headers.set('Authorization', `Bearer ${useUserStore.getState().accessToken}`)
    return rawTransport(retry)
  },
}
```

`Request.clone()` 把底层 body 流一分为二，两份各自独立可读。原始请求照常发出，没动过的那份克隆存进 `WeakMap`，键就是原始 `Request` 实例；openapi-fetch 会把这同一个实例一路带到 `onResponse`。请求结束后这个 `WeakMap` 条目自动回收，不用手清。

遇到 401，依次发生：

1. **跳过刷新和登录接口自身**：`/auth/refresh` 或 `/auth/login` 返回的 401 是真实凭证失败，不是令牌过期；把它也塞进刷新流程会死循环。
2. **`refreshOnce()` 并发合流**：同一时刻好几个请求一起 401，也只发一次 `/auth/refresh`，让大家都等同一个 promise：
   ```ts
   let refreshing: Promise<boolean> | null = null
   function refreshOnce(): Promise<boolean> {
     refreshing ??= doRefresh().finally(() => { refreshing = null })
     return refreshing
   }
   ```
3. **刷新失败**（没有 refreshToken、网络错误、`code` 非零、或没有 `data`）：清空会话，再 `gotoLogin()` 走 `window.location.assign('/login')` 整页跳登录。用 `window.location` 而不是 router 跳转，好处是 `client.ts` 压根不 import 路由，两者之间没有静态循环依赖；令牌失效后整页重载，顺带把内存里的旧状态清干净。
4. **刷新成功**：拿发出前存的克隆重建请求，补上刚刷新出来的新令牌，再用**裸 `rawTransport(retry)`** 重放。GET/HEAD 本来没克隆，直接用原始请求。这里特意不再走一次 `client.GET/POST(...)`：再走 `client`，两个中间件会在这次重放上又跑一遍，万一新令牌也被拒又是一次 401，就递归进下一轮刷新。

### 为什么 `doRefresh` 用 `bare` 而不是 `client`

```ts
async function doRefresh(): Promise<boolean> {
  const user = useUserStore.getState()
  if (!user.refreshToken) return false

  const { data, error } = await bare.POST('/api/v1/auth/refresh', {
    body: { refreshToken: user.refreshToken },
  })
  const envelope = data as { code?: number; data?: unknown } | undefined
  if (error || !envelope || envelope.code !== 0 || !envelope.data) return false

  useUserStore.getState().setSession(envelope.data as Parameters<typeof user.setSession>[0])
  return true
}
```

`bare` 是用同一份 schema 建的第二个 `openapi-fetch` 客户端，但不挂任何中间件。刷新请求走 `bare` 就不递归：就算刷新本身失败，比如 `refreshToken` 也过期、接口照样答 401，这个 401 也进不了 `refreshMiddleware.onResponse`，因为 `bare` 上根本没有中间件链可以递归。`onResponse` 里那道跳过 `/auth/refresh`、`/auth/login` 的 URL 判断只是第二道保险，顺带覆盖经 `client` 调用登录失败的情况。刷新请求自身能防住递归，根子上靠的是它压根不在 `client` 的中间件链上。

## 开发代理与 CORS

类型化客户端默认浏览器同源访问 `/api`：`client` 的 `baseUrl` 为空，请求走一个看起来相对的 URL，没做任何跨域处理。`gen:api` 不一样，它压根不经过浏览器：命令由 Node 直接发给写死的 `http://localhost:5100/openapi/v1.json`，不走 dev proxy，自然也没有 CORS 这回事。后端不在 5100，就得改 `package.json` 里那行脚本，`TENON_API_TARGET` 对它无效。本地开发时，后端跑在 `:5100`、dev server 跑在 `:5174`，端口不一样，总得有个东西把这道缝补上。

补这道缝的就是 `vite.config.ts` 里的 dev 代理：

```ts
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

server: {
  port: 5174,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
    '/hub': { target: apiTarget, changeOrigin: true, ws: true }, // SignalR 通知 Hub
  },
},
```

它把 `:5174` 上的 `/api/*`、`/openapi/*` 转发给后端；`/hub` 是 SignalR 实时通知的 WebSocket 通道，`ws: true` 让升级请求也走反代。浏览器自始至终只看到 `:5174` 一个源，不存在跨域。目标默认 `http://localhost:5100`，后端在别处就在起 Vite 前设一下 `TENON_API_TARGET`。

没有这层代理会怎样？类型化客户端的请求、`gen:api` 的 schema 拉取，都会直接打到后端的源上。后端 CORS 默认 deny-all，响应还没到 `unwrap` 或 `openapi-typescript`，就被浏览器（或 `gen:api` 的 fetch）拒了。是这层代理，让请求层「同源」这个前提在本地成立。

::: tip 生产环境没有这层代理
`npm run dev` 的代理只在开发期存在。生产构建出的 `web-react/dist` 是纯静态文件，请求怎么到后端，要在部署时自己解决：后端顺带托管前端产物，或者 nginx/Caddy 反代，都是同源，不用配 CORS。只有前端和后端真跨源，比如前端上 CDN、后端独立域名，才需要动 `TenonAdmin:Api:Cors:AllowedOrigins`，方案见[部署路线 C：真跨源](/zh/guide/deployment/route-c)。
:::

完整 server 配置与联调别名见[项目结构与启动](/zh/frontend-react/structure)。
