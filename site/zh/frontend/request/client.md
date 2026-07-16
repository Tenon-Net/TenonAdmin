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

**上一节:** [HTTP 请求层](/zh/frontend/request/)
**下一节:** [开发代理与 CORS](/zh/frontend/request/proxy)
