import createClient, { type Middleware } from 'openapi-fetch'
import type { paths } from './schema'
import { useUserStore } from '@/stores/user'

/**
 * 以 `dev` 上 `web/src/api/client.ts` 为蓝本重写(**不是**从共享层搬的那版:那版为服务两个模板抽了
 * `ApiAdapter`/`createApiClient` 工厂,现在只有一个宿主,工厂没有存在理由)。宿主耦合直接写死:
 * 取会话走 zustand 的 `getState()`(Pinia 那边是 `useUserStore()`),跳登录走整页导航(见下)。
 *
 * 下面三个机制是**逐字保留**的,它们各自防的失败模式都极难在事后归因:
 *   ① `replayable` 请求克隆重放 —— 令牌恰好在一次 POST 中途过期时不丢请求体
 *   ② `refreshOnce` 并发 401 合流 —— 一次刷新,不是 N 次
 *   ③ `bare` 客户端不挂刷新中间件 —— 刷新自身 401 时不递归
 */

// 默认空 baseUrl = 同源:schema 的 path 键已含 /api/v1,dev 下由 Vite proxy 反代到后端(默认 :5100),
// 生产下由 nginx 反代或后端自己托管 dist。只有前端与 API 真的不同源(CDN / 独立域名)时才需要
// 构建期给 VITE_API_BASE,此时后端还必须配 TenonAdmin:Api:Cors:AllowedOrigins(默认 deny-all)。
const baseUrl = import.meta.env.VITE_API_BASE ?? ''
export const client = createClient<paths>({ baseUrl })
// 刷新专用客户端:不挂刷新中间件,避免刷新自身 401 触发递归。
const bare = createClient<paths>({ baseUrl })

/** 请求前注入 Bearer(请求时读 store,始终拿最新令牌)。 */
const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = useUserStore.getState().accessToken
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
  const user = useUserStore.getState()
  if (!user.refreshToken) return false
  const { data, error } = await bare.POST('/api/v1/auth/refresh', {
    body: { refreshToken: user.refreshToken },
  })
  const env = data as { code?: number; data?: unknown } | undefined
  if (error || !env || env.code !== 0 || !env.data) return false
  // 重新取一次 state:`setSession` 是 store 上的 action,而上面那份 `user` 是取快照时的对象。
  // zustand 的 action 引用稳定,这里取新的只为语义清楚。
  useUserStore.getState().setSession(env.data as Parameters<typeof user.setSession>[0])
  return true
}

/**
 * 会话已死,离开当前页。
 *
 * Vue 侧是 `router.replace('/login')`(软跳),这里用**整页导航**。不是偷懒的替代品,是有意的:
 * B6 的动态路由走 `useRoutes(routes)`(组件 API),模块级拿不到 router 实例,软跳需要额外接缝;
 * 而会话死亡这条路径上整页重载反而更干净 —— 内存里的动态路由、字典缓存、标签页一次清空,
 * 不必指望每个 store 的 reset 都写全。行为差异对用户不可见(两边都不带 redirect 参数)。
 *
 * ponytail: 整页重载,若将来要保留「登录后回到原页」再换成路由级跳转 + redirect query。
 */
function gotoLogin(): void {
  if (window.location.pathname !== '/login') window.location.assign('/login')
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
      useUserStore.getState().clear()
      gotoLogin()
      return response
    }
    // 重放:优先用发出前的克隆副本(body 未被消费),无副本的 GET 直接用原请求;
    // 裸 fetch 绕过中间件,手动补新令牌。
    const base = replayable.get(request) ?? request
    const retry = new Request(base, { headers: new Headers(base.headers) })
    retry.headers.set('Authorization', `Bearer ${useUserStore.getState().accessToken}`)
    return fetch(retry)
  },
}

client.use(authMiddleware)
client.use(refreshMiddleware)
