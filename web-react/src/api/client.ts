import createClient, { type Middleware } from 'openapi-fetch'
import type { paths } from './schema'
import { useUserStore, isCookieSession } from '@/stores/user'
import { REAUTH_REQUIRED_CODE, REAUTH_RETRY_HEADER, requestReauth } from './reauthGate'

/** 与后端 AuthCookieNames 对齐(禁止硬编码散落)。 */
export const AUTH_CSRF_COOKIE = 'tenon_csrf'
export const AUTH_CSRF_HEADER = 'X-Tenon-CSRF'

const baseUrl = import.meta.env.VITE_API_BASE ?? ''
const rawTransport = globalThis.fetch

/**
 * credentials:include —— Level3 依赖 HttpOnly refresh Cookie + 可读 CSRF Cookie。
 * 非 Level3 同源默认也无副作用;跨源时宿主须配 CORS AllowedOrigins(与部署文档一致)。
 */
const client = createClient<paths>({ baseUrl, fetch: rawTransport, credentials: 'include' })
const bare = createClient<paths>({ baseUrl, fetch: rawTransport, credentials: 'include' })
const replayable = new WeakMap<Request, Request>()
let refreshing: Promise<boolean> | null = null

/** 读双提交 CSRF Cookie(document.cookie;HttpOnly refresh 读不到,正合预期)。 */
export function readCsrfCookie(): string {
  if (typeof document === 'undefined') return ''
  const prefix = `${AUTH_CSRF_COOKIE}=`
  for (const part of document.cookie.split(';')) {
    const s = part.trim()
    if (s.startsWith(prefix)) {
      try {
        return decodeURIComponent(s.slice(prefix.length))
      } catch {
        return s.slice(prefix.length)
      }
    }
  }
  return ''
}

function attachCsrf(headers: Headers): void {
  const csrf = readCsrfCookie()
  if (csrf) headers.set(AUTH_CSRF_HEADER, csrf)
}

const authMiddleware: Middleware = {
  onRequest({ request }) {
    const token = useUserStore.getState().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    // Level3:状态改变写请求(及任何带 refresh Cookie 的 POST)需双提交 CSRF
    attachCsrf(request.headers)
    return request
  },
}

async function doRefresh(): Promise<boolean> {
  const user = useUserStore.getState()
  const cookie = isCookieSession(user)
  // body 模式必须持有 refresh;cookie 模式 refresh 在 HttpOnly Cookie,本地可为空
  if (!cookie && !user.refreshToken) return false

  // bare 不挂 auth/refresh 中间件;手动补 CSRF(Level3 刷新自身也是状态改变 + 带 Cookie)
  const headers: Record<string, string> = {}
  const csrf = readCsrfCookie()
  if (csrf) headers[AUTH_CSRF_HEADER] = csrf

  const { data, error } = await bare.POST('/api/v1/auth/refresh', {
    // cookie 模式 body 可空串;后端 ResolveRefreshToken 优先 body,空则读 Cookie
    body: { refreshToken: cookie ? '' : user.refreshToken },
    headers,
  })
  const envelope = data as { code?: number; data?: unknown } | undefined
  if (error || !envelope || envelope.code !== 0 || !envelope.data) return false

  useUserStore.getState().setSession(envelope.data as Parameters<typeof user.setSession>[0])
  return true
}

function refreshOnce(): Promise<boolean> {
  refreshing ??= doRefresh().finally(() => {
    refreshing = null
  })
  return refreshing
}

/**
 * Level3 F5/深链:内存 access 已空、localStorage 仅记 cookie 会话标记时,
 * 凭 HttpOnly refresh Cookie 静默换发 access。非 cookie 会话直接 false。
 */
export async function tryRestoreCookieSession(): Promise<boolean> {
  const s = useUserStore.getState()
  if (s.accessToken) return true
  if (!isCookieSession(s)) return false
  const ok = await refreshOnce()
  if (!ok) useUserStore.getState().clear()
  return ok
}

function gotoLogin(): void {
  if (window.location.pathname !== '/login') window.location.assign('/login')
}

const refreshMiddleware: Middleware = {
  onRequest({ request }) {
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
    const retry = new Request(base, { headers: new Headers(base.headers), credentials: base.credentials })
    retry.headers.set('Authorization', `Bearer ${useUserStore.getState().accessToken}`)
    attachCsrf(retry.headers)
    return rawTransport(retry)
  },
}

/** 403 + 40024 → 再认证弹窗 → 成功后重放一次(防死循环靠重试头)。 */
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

    if (!(await requestReauth())) return response

    const base = replayable.get(request) ?? request
    const retry = new Request(base, { headers: new Headers(base.headers), credentials: base.credentials })
    const token = useUserStore.getState().accessToken
    if (token) retry.headers.set('Authorization', `Bearer ${token}`)
    attachCsrf(retry.headers)
    retry.headers.set(REAUTH_RETRY_HEADER, '1')
    return rawTransport(retry)
  },
}

client.use(authMiddleware)
client.use(refreshMiddleware)
client.use(reauthMiddleware)

/** 应用默认客户端。 */
export { client }
