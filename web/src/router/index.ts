import { createRouter, createWebHistory } from 'vue-router'
import { staticRoutes } from './routes'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'
import { useTabsStore } from '@/stores/tabs'

export const router = createRouter({
  history: createWebHistory(),
  routes: staticRoutes,
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

  // 登录页是唯一免认证页;已登录再访问则回首页。
  if (to.name === 'login') return user.isLoggedIn ? { path: '/', replace: true } : true

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
      if (to.name === 'module') return { path: '/', replace: true }
      return to.fullPath // 重解析当前 URL(此时动态路由已就绪)
    } catch {
      user.clear()
      return { path: '/login', replace: true }
    }
  }

  return true
})

// 记录已访问页为标签(动态路由就绪后触发,F5 重解析也会命中)。
router.afterEach((to) => {
  if (to.meta.public) return
  if (['login', 'module', 'not-found'].includes(to.name as string)) return
  if (!to.matched.some((r) => r.name === 'layout')) return
  useTabsStore().addTab(to)
})
