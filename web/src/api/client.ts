import createClient, { type Middleware } from 'openapi-fetch'
import type { paths } from './schema'
import { useUserStore } from '@/stores/user'
import { REAUTH_REQUIRED_CODE, REAUTH_RETRY_HEADER, requestReauth } from './reauthGate'

// 默认空 baseUrl = 同源:schema 的 path 键已含 /api/v1,dev 下由 Vite proxy 反代到后端(默认 :5100),
// 生产下由 nginx 反代或后端自己托管 dist(见 docs/deployment.md 路线 A/B)。
// 只有前端与 API 真的不同源(CDN / 独立域名)时才需要构建期给 VITE_API_BASE=https://api.example.com,
// 此时后端还必须配 TenonAdmin:Api:Cors:AllowedOrigins(默认 deny-all)。
const baseUrl = import.meta.env.VITE_API_BASE ?? ''

/** 与后端 AuthCookieNames 对齐(禁硬编码纪律的前端镜像)。 */
const CSRF_COOKIE = 'tenon_csrf'
const CSRF_HEADER = 'X-Tenon-CSRF'

/**
 * 读 document.cookie 中的可读 Cookie(双提交 CSRF 的 tenon_csrf)。
 * HttpOnly 的 tenon_rt 读不到,只能靠 credentials 自动携带。
 */
export function readCookie(name: string): string {
  if (typeof document === 'undefined') return ''
  const parts = document.cookie.split(';')
  for (const part of parts) {
    const i = part.indexOf('=')
    if (i < 0) continue
    const k = part.slice(0, i).trim()
    if (k === name) return decodeURIComponent(part.slice(i + 1).trim())
  }
  return ''
}

/** 写请求是否需要附 CSRF(有可读 CSRF Cookie 即附;无 Cookie 时与非 Level3 行为一致)。 */
function isMutating(method: string): boolean {
  const m = method.toUpperCase()
  return m !== 'GET' && m !== 'HEAD' && m !== 'OPTIONS'
}

function attachCsrf(headers: Headers) {
  const csrf = readCookie(CSRF_COOKIE)
  if (csrf) headers.set(CSRF_HEADER, csrf)
}

// credentials:'include'——Cookie 会话(Session:CookieMode)静默刷新/登出依赖 HttpOnly refresh Cookie;
// 同源默认也会带 Cookie,显式 include 覆盖 VITE_API_BASE 跨源场景(需后端 CORS AllowCredentials)。
export const client = createClient<paths>({ baseUrl, credentials: 'include' })
// 刷新专用客户端:不挂刷新中间件,避免刷新自身 401 触发递归。
const bare = createClient<paths>({ baseUrl, credentials: 'include' })

/** 请求前注入 Bearer + 写操作 CSRF。请求时读 store,始终拿最新令牌。 */
const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = useUserStore().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    if (isMutating(request.method)) attachCsrf(request.headers)
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

/** 单飞静默刷新;路由守卫 F5 重建 Cookie 会话时也可调用。 */
export function refreshOnce(): Promise<boolean> {
  refreshing ??= doRefresh().finally(() => {
    refreshing = null
  })
  return refreshing
}

/**
 * 确保有可用 accessToken:已有则 true;Level3 Cookie 会话或 body refresh 则尝试静默刷新。
 * 供路由守卫在 F5/深链时恢复内存令牌。
 */
export async function ensureAccessToken(): Promise<boolean> {
  const user = useUserStore()
  if (user.accessToken) return true
  if (!user.cookieSession && !user.refreshToken) return false
  return refreshOnce()
}

async function doRefresh(): Promise<boolean> {
  const user = useUserStore()
  // Cookie 会话:body 不带 refresh(服务端读 tenon_rt);body 模式:必须有 refreshToken
  if (!user.cookieSession && !user.refreshToken) return false

  const headers: Record<string, string> = {}
  if (user.cookieSession || readCookie(CSRF_COOKIE)) {
    const csrf = readCookie(CSRF_COOKIE)
    if (csrf) headers[CSRF_HEADER] = csrf
  }

  const { data, error } = await bare.POST('/api/v1/auth/refresh', {
    // Cookie 模式可空 body;带空 refreshToken 兼容旧合同(服务端优先 body 再 Cookie)
    body: user.cookieSession ? { refreshToken: '' } : { refreshToken: user.refreshToken },
    headers,
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
      const { useAuthStore } = await import('@/stores/auth')
      useAuthStore().reset()
      const { router, resetRouter } = await import('@/router') // 惰性引入,避免与 router 静态循环依赖
      resetRouter()
      if (router.currentRoute.value.path !== '/login') router.replace('/login')
      return response
    }
    // 重放:优先用发出前的克隆副本(body 未被消费),无副本的 GET 直接用原请求;裸 fetch 绕过中间件,手动补新令牌 + CSRF。
    const base = replayable.get(request) ?? request
    const retry = new Request(base, { headers: new Headers(base.headers), credentials: 'include' })
    retry.headers.set('Authorization', `Bearer ${useUserStore().accessToken}`)
    if (isMutating(retry.method)) attachCsrf(retry.headers)
    return fetch(retry)
  },
}

/**
 * 403 + 40024(ReauthRequired) → 弹再认证 → 成功后重放一次。
 * 与 refresh 同形:依赖 onRequest 预克隆 body;重试头防死循环。
 */
const reauthMiddleware: Middleware = {
  async onResponse({ request, response }) {
    if (response.status !== 403) return response
    if (request.headers.get(REAUTH_RETRY_HEADER) === '1') return response
    if (request.url.includes('/api/v1/auth/reauth') || request.url.includes('/api/v1/auth/login')) {
      return response
    }

    let code: number | undefined
    try {
      const body = (await response.clone().json()) as { code?: number }
      code = body?.code
    } catch {
      return response
    }
    if (code !== REAUTH_REQUIRED_CODE) return response

    const granted = await requestReauth()
    if (!granted) return response

    const base = replayable.get(request) ?? request
    const retry = new Request(base, { headers: new Headers(base.headers), credentials: 'include' })
    const token = useUserStore().accessToken
    if (token) retry.headers.set('Authorization', `Bearer ${token}`)
    if (isMutating(retry.method)) attachCsrf(retry.headers)
    retry.headers.set(REAUTH_RETRY_HEADER, '1')
    return fetch(retry)
  },
}

client.use(authMiddleware)
client.use(refreshMiddleware)
client.use(reauthMiddleware)
