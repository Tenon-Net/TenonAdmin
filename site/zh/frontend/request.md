# HTTP 请求层

前端每一次后端调用都走同一条流水线:后端 OpenAPI 生成的 schema 给客户端上类型,两个中间件分别管认证和令牌刷新,`unwrap` 把后端可能返回的两种响应形状都收拢成统一结果,视图层才拿到手。本页按顺序拆开每一环。

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

## 重新生成契约:`gen:api`

```bash
npm run gen:api   # openapi-typescript http://localhost:5100/openapi/v1.json -o src/api/schema.d.ts
```

- 后端必须先跑起来 —— 脚本要向一个真实运行中的服务器拉 `/openapi/v1.json`(默认 `http://localhost:5100`;后端跑在别处时看 `web/vite.config.ts` 的 `TENON_API_TARGET` 对应的 dev 代理目标)。
- `src/api/schema.d.ts` 是**生成产物**,禁止手改 —— 改后端的接口/DTO,重新生成即可;手改的内容下次生成会被无声覆盖。
- `src/api/client.ts` 的 `createClient<paths>()` 就是拿这份文件当类型源,所以每一次 `client.GET/POST/PUT/DELETE` 调用从路径参数、查询参数、请求体到响应形状,全链路都是后端真实契约推出来的类型。

## 本节内容

- [类型化客户端与两个中间件](/zh/frontend/request) —— `client.ts` 的两个中间件:认证与 401 刷新
- [开发代理与 CORS](/zh/frontend/request) —— 为什么请求层离不开 dev 代理
- [对接后端响应](/zh/frontend/api-contract) —— `unwrap`、`ApiError`、分页助手与错误文案



---

<!-- TODO(rewrite): merged from client.md -->

# 类型化客户端与两个中间件

```ts
const baseUrl = import.meta.env.VITE_API_BASE ?? ''
export const client = createClient<paths>({ baseUrl })
// 刷新专用客户端:不挂任何中间件,刷新请求自己的 401 就没有递归的入口。
const bare = createClient<paths>({ baseUrl })
```

`baseUrl` 默认为空 —— schema 的 path 键本身已经带 `/api/v1`,`/api` 走同源(dev 下由 Vite 代理到后端,生产下由反代或后端自托管)。只有前端和 API 真的跨域时才需要设 `VITE_API_BASE`。

两个中间件挂在 `client` 上(不挂在 `bare` 上,原因见下文):

## 认证中间件

```ts
const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = useUserStore().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    return request
  },
}
```

请求发出时才去 store 读令牌(不是模块加载时读一次存住),所以每次都能拿到最新的令牌 —— 包括刚刚才刷新出来的那个。

## 401 刷新中间件,以及为什么重放需要一份克隆

这是 `client.ts` 里最不直观的一段。问题在于:`Request` 的 body 是个流,只能被读一次。一个 POST/PUT 请求被判 401 后,流程要去刷新令牌、再拿同一个请求重放 —— 但等响应回来的时候,原始请求的 body 早就被 `fetch` 消费掉了,原样重放会把 body 发丢。

解法是在请求真正发出**之前**、body 流还没被碰过的时候,先克隆一份:

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

`Request.clone()` 会把底层的 body 流一分为二,各自独立可读 —— 原始请求照常发出去,没被动过的克隆存进一个以原始 `Request` 实例为键的 `WeakMap`(openapi-fetch 会把这同一个实例一路带到 `onResponse`;请求结束后这个 `WeakMap` 条目自动被回收,不用手动清理)。

遇到 401,依次发生:

1. **跳过刷新/登录接口自身** —— `/auth/refresh` 或 `/auth/login` 返回的 401 是真实的凭证失败,不是令牌过期;把它也塞进刷新流程会死循环。
2. **`refreshOnce()`** —— 并发合流:如果同一时刻有好几个请求同时 401,只发一次 `/auth/refresh`,大家都等同一个 promise:
   ```ts
   let refreshing: Promise<boolean> | null = null
   function refreshOnce(): Promise<boolean> {
     refreshing ??= doRefresh().finally(() => { refreshing = null })
     return refreshing
   }
   ```
3. **刷新失败**(没有 refreshToken、网络错误、`code` 非零、或没有 `data`)—— 清空会话,跳转 `/login`(router 用惰性 import,避免与 `client.ts` 形成静态循环依赖)。
4. **刷新成功** —— 用发出前存的克隆副本重建请求(GET/HEAD 本来就没克隆,直接用原始请求),补上刚刷新出来的新令牌,用**裸 `fetch()`** 重放 —— 而不是再走一次 `client.GET/POST(...)`。再走 `client` 会让两个中间件在这次重放上再跑一遍,万一新令牌也被拒(又是一次 401),就会递归进下一轮刷新。

## 为什么 `doRefresh` 用 `bare` 而不是 `client`

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

`bare` 是用同一份 schema 建的第二个 `openapi-fetch` 客户端,但**不挂任何中间件**。刷新请求走 `bare`,意味着一次失败的刷新(比如 refreshToken 本身也过期了,接口照样答 401)根本不会再进 `refreshMiddleware.onResponse` —— `bare` 上没有中间件链可供递归。`onResponse` 里那道跳过 `/auth/refresh`/`/auth/login` 的 URL 判断是第二道保险,顺带也覆盖了经 `client` 调用登录失败的情况;刷新请求自身的防递归,根本上是靠它压根不在 `client` 的中间件链上。



---

<!-- TODO(rewrite): merged from proxy.md -->

# 开发代理与 CORS

类型化客户端(`src/api/client.ts`)和 `gen:api` 都默认浏览器是同源访问 `/api` 和 `/openapi` 的 —— `client` 的 `baseUrl` 默认为空,`gen:api` 拉取 `/openapi/v1.json` 时用的也是一个看起来相对的 URL,两者都没做任何跨域处理。本地开发时后端跑在 `:5100`,dev server 跑在 `:5173`,端口不一样,总得有个东西把这道缝补上,两边才能正常工作。

补这道缝的就是 `vite.config.ts` 里的 dev 代理:

```ts
const apiTarget = process.env.TENON_API_TARGET ?? 'http://localhost:5100'

server: {
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
  },
},
```

它把 `:5173` 上的 `/api/*`、`/openapi/*` 请求转发给后端,浏览器自始至终只看到一个源。目标地址默认是 `http://localhost:5100`;后端跑在别处时,启动 Vite 前设置 `TENON_API_TARGET` 即可。

没有这层代理,类型化客户端的请求和 `gen:api` 的 schema 拉取都会直接打到后端的源上 —— 而后端 CORS 默认 deny-all,浏览器(或者 `gen:api` 的 fetch)会在响应传到 `unwrap` 或 `openapi-typescript` 之前就把它拒了。是这层代理让请求层"同源"这个前提在本地成立;生产环境下则是反向代理扮演同样的角色。

完整代理配置与联调别名见[项目结构与启动](/zh/frontend/structure)。

