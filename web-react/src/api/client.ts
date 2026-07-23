import createClient, { type Middleware } from 'openapi-fetch'
import type { paths } from './schema'
import { useUserStore } from '@/stores/user'

const baseUrl = import.meta.env.VITE_API_BASE ?? ''
const rawTransport = globalThis.fetch
const client = createClient<paths>({ baseUrl, fetch: rawTransport })
const bare = createClient<paths>({ baseUrl, fetch: rawTransport })
const replayable = new WeakMap<Request, Request>()
let refreshing: Promise<boolean> | null = null

const authMiddleware: Middleware = {
  onRequest({ request }) {
    const token = useUserStore.getState().accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    return request
  },
}

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

function refreshOnce(): Promise<boolean> {
  refreshing ??= doRefresh().finally(() => {
    refreshing = null
  })
  return refreshing
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
    const retry = new Request(base, { headers: new Headers(base.headers) })
    retry.headers.set('Authorization', `Bearer ${useUserStore.getState().accessToken}`)
    return rawTransport(retry)
  },
}

client.use(authMiddleware)
client.use(refreshMiddleware)

/** 应用默认客户端。 */
export { client }
