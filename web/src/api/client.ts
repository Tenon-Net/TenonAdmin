import createClient, { type Middleware } from 'openapi-fetch'
import type { paths } from './schema'
import { useUserStore } from '@/stores/user'

// 默认空 baseUrl = 同源:schema 的 path 键已含 /api/v1,dev 下由 Vite proxy 反代到后端(默认 :5100),
// 生产下由 nginx 反代或后端自己托管 dist(见 docs/deployment.md 路线 A/B)。
// 只有前端与 API 真的不同源(CDN / 独立域名)时才需要构建期给 VITE_API_BASE=https://api.example.com,
// 此时后端还必须配 TenonAdmin:Api:Cors:AllowedOrigins(默认 deny-all)。
const baseUrl = import.meta.env.VITE_API_BASE ?? ''
export const client = createClient<paths>({ baseUrl })
// 刷新专用客户端:不挂刷新中间件,避免刷新自身 401 触发递归。
const bare = createClient<paths>({ baseUrl })

/** 请求前注入 Bearer(请求时读 store,始终拿最新令牌)。 */
const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = useUserStore().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    return request
  },
}

// 令牌过期重放:Request 的 body 是一次性流,首次 fetch 就被消费,直接重放会丢 body
//(GET 无 body 不受影响,但令牌恰好在一次 POST/PUT 时过期就会丢请求体)。
// 发出前克隆一份副本(clone 会 tee body 流,与原请求各读各的),重放时用这份未消费的副本。
// 键是发出前的 Request 实例(openapi-fetch 把它一路带到 onResponse),WeakMap 随请求回收自动清理。
const replayable = new WeakMap<Request, Request>()

// 并发 401 合流到同一次刷新。
let refreshing: Promise<boolean> | null = null
function refreshOnce(): Promise<boolean> {
  refreshing ??= doRefresh().finally(() => {
    refreshing = null
  })
  return refreshing
}

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

/** 401 → 刷新一次并重放原请求;刷新失败 → 清会话 + 跳登录。 */
const refreshMiddleware: Middleware = {
  onRequest({ request }) {
    // 带 body 的写请求发出前存一份可重放副本(GET/HEAD 无 body,不必克隆)。
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
      const { router } = await import('@/router') // 惰性引入,避免与 router 静态循环依赖
      if (router.currentRoute.value.path !== '/login') router.replace('/login')
      return response
    }
    // 重放:优先用发出前的克隆副本(body 未被消费),无副本的 GET 直接用原请求;裸 fetch 绕过中间件,手动补新令牌。
    const base = replayable.get(request) ?? request
    const retry = new Request(base, { headers: new Headers(base.headers) })
    retry.headers.set('Authorization', `Bearer ${useUserStore().accessToken}`)
    return fetch(retry)
  },
}

client.use(authMiddleware)
client.use(refreshMiddleware)
