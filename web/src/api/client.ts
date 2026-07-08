import createClient, { type Middleware } from 'openapi-fetch'
import type { paths } from './schema'
import { useUserStore } from '@/stores/user'

// baseUrl 为空:schema 的 path 键已含 /api/v1,直接作用于当前源(:5173),由 Vite proxy 反代到 :5000。
export const client = createClient<paths>({ baseUrl: '' })
// 刷新专用客户端:不挂刷新中间件,避免刷新自身 401 触发递归。
const bare = createClient<paths>({ baseUrl: '' })

/** 请求前注入 Bearer(请求时读 store,始终拿最新令牌)。 */
const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = useUserStore().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    return request
  },
}

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
    // 重放:原 Request 已消费,克隆一份;裸 fetch 绕过中间件,手动补新令牌。
    const retry = new Request(request, { headers: new Headers(request.headers) })
    retry.headers.set('Authorization', `Bearer ${useUserStore().accessToken}`)
    return fetch(retry)
  },
}

client.use(authMiddleware)
client.use(refreshMiddleware)
