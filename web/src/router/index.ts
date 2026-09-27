import { createRouter, createWebHistory } from 'vue-router'
import { staticRoutes } from './routes'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'
import { useTabsStore } from '@/stores/tabs'
import { loadingBar } from '@/lib/loadingBar'
import { ensureAccessToken } from '@/api/client'

export const router = createRouter({
  history: createWebHistory(),
  routes: staticRoutes,
})

// 路由加载进度条:懒加载 chunk 与 F5 重建菜单(enterInitial)期间给出反馈。
router.beforeEach(() => {
  loadingBar.start()
})
router.afterEach(() => {
  loadingBar.finish()
})
router.onError(() => {
  loadingBar.error()
})

// 动态路由名登记,用于登出/切应用时精确移除。
let dynamicNames: Array<string | symbol> = []
export function registerDynamic(name: string | symbol) {
  dynamicNames.push(name)
}
export function resetRouter() {
  for (const n of dynamicNames) if (router.hasRoute(n)) router.removeRoute(n)
  dynamicNames = []
}

router.beforeEach(async (to) => {
  const user = useUserStore()
  const auth = useAuthStore()

  // Level3 Cookie 会话:access 只在内存,F5 后靠 cookieSession 标记 + HttpOnly refresh 静默换发。
  // 非 Level3 令牌已 localStorage 水合,ensureAccessToken 立即 true。
  if (!user.accessToken && (user.cookieSession || user.refreshToken)) {
    const ok = await ensureAccessToken()
    if (!ok) user.clear()
  }

  // 登录页是唯一免认证页;已登录再访问则回首页。
  // 例外:SSO 未绑定 pendingLink / SSO 后 TOTP 挑战——必须停在登录页完成账密或二次验证。
  // 若仍带着残留会话进 /login,会误弹回首页,看起来像「解绑后 GitHub 仍直接登录」。
  if (to.name === 'login') {
    const needReauth = !!(to.query.pendingLink || to.query.totpChallenge)
    if (needReauth) {
      if (user.accessToken || user.refreshToken || user.cookieSession) {
        resetRouter()
        auth.reset()
        user.clear()
      }
      return true
    }
    return user.isLoggedIn ? { path: '/', replace: true } : true
  }

  // 公开的 OAuth 回调和 MFA 绑定/恢复页不能被登录守卫送回登录页。
  // catch-all 404 也标记为 public,但 F5/深链首次解析时它只是动态路由
  // 尚未恢复的临时匹配，不能在这里提前放行。
  if (to.meta.public && to.name !== 'not-found') return true

  if (!user.isLoggedIn) return { path: '/login', replace: true }

  // 强制改密守卫(§14):管理员建号/重置后首登,未改密前只允许停留在改密页,阻断其它一切导航
  // (含应用选择/门户重建)。改密页是静态路由,无需动态路由就绪即可渲染,故放在 routesReady 重建之前,
  // 直接放行改密页避免"重建→选应用→又被弹回"的循环。改密成功后现有流程强制登出重登,标志由后端清零。
  if (user.userInfo?.mustChangePassword) {
    return to.path === '/personal/password' ? true : { path: '/personal/password', replace: true }
  }

  // 刷新白屏守卫:动态路由只活在内存,F5/深链时 routesReady=false → 重建后重解析。
  // 注意:不能用 to.meta.public 短路——未注册的动态路由会先命中 catch-all(404),
  // 若按 public 放行就会错显 404 而非重建。
  if (!auth.routesReady) {
    try {
      const { useModule } = await import('@/composables/useModule')
      const res = await useModule().enterInitial()
      if (res.chooser) return to.name === 'module' ? true : { path: '/module', replace: true }
      // 选应用页放行:enterInitial 已填好 auth.modules,渲染所需数据齐备。
      // 不能弹回 '/' —— 那样默认应用一旦设定就再无入口改它(右上角九宫格正是导航到这里)。
      if (to.name === 'module') return true
      // 目标是 '/' 时不能 return to.fullPath——那是重定向到自身,'/' 已无静态 redirect,会被判成无限重定向。
      // 此刻菜单树已就绪,直接给出首页。
      if (to.path === '/') return { path: auth.homePath, replace: true }
      // 动态路由是在本次导航的守卫中追加的。仅返回同一个 path 可能继续沿用
      // 本次导航已解析出的 catch-all 记录；先用更新后的 matcher 解析一次，再
      // 按动态路由名返回，确保当前深链真正重新匹配。
      const resolved = router.resolve(to.fullPath)
      if (resolved.name && resolved.name !== 'not-found') {
        return { name: resolved.name, params: resolved.params, query: to.query, hash: to.hash, replace: true }
      }
      return { path: to.path, query: to.query, hash: to.hash, replace: true }
    } catch {
      user.clear()
      return { path: '/login', replace: true }
    }
  }

  // '/' 落到当前应用自己的首页。放在守卫里(而非 layout 的 redirect)——redirect 在 resolve 阶段求值,
  // 早于本守卫,那时 menuTree 还空、动态路由未建,算出的首页必然是错的。
  if (to.path === '/') return { path: auth.homePath, replace: true }

  return true
})

// 记录已访问页为标签(动态路由就绪后触发,F5 重解析也会命中)。
router.afterEach((to) => {
  if (to.meta.public) return
  if (['login', 'module', 'not-found', 'personal'].includes(to.name as string)) return
  if (!to.matched.some((r) => r.name === 'layout')) return
  useTabsStore().addTab(to)
})
