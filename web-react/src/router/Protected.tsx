import { lazy, Suspense, useEffect, useMemo, useState, type ComponentType } from 'react'
import { Spin } from 'antd'
import { Navigate, useLocation, useRoutes } from 'react-router-dom'
import { useUserStore, isLoggedIn, isCookieSession } from '@/stores/user'
import { useAuthStore, homePath } from '@/stores/auth'
import { tryRestoreCookieSession } from '@/api/client'
import { enterInitial } from '@/composables/useModule'
import { buildRoutes } from './buildRoutes'
import { buildDetailRoutes } from './detailRoutes'
import ModuleChooser from '@/views/module'
import { LayoutShell } from '@/layouts/LayoutShell'

// 个人中心五页 = 静态路由(不进后端菜单,经顶栏下拉/铃铛入口);lazy 以 code-split(notice 拉 pro-components,别进主 chunk)。
// **已在 buildRoutes 的 glob 排除 `personal/**`**,否则静态+动态双 import 无法 code-split。
const ProfilePage = lazy(() => import('@/views/personal/profile'))
const PasswordPage = lazy(() => import('@/views/personal/password'))
const NoticePage = lazy(() => import('@/views/personal/notice'))
const SessionsPage = lazy(() => import('@/views/personal/sessions'))
const BindingsPage = lazy(() => import('@/views/personal/bindings'))
const NotFoundPage = lazy(() => import('@/views/error/NotFoundPage'))
const lazyEl = (El: ComponentType) => (
  <Suspense fallback={null}>
    <El />
  </Suspense>
)

/**
 * 受保护区的守卫,三条按 Vue 侧 `router/index.ts` 的顺序:
 *   ① 未登录 → `/login`
 *   ② 强制改密(`mustChangePassword`)→ 锁死改密页,阻断其它一切(含门户重建/选应用)
 *   ③ 动态路由未就绪(F5/深链)→ 跑一次 `enterInitial()` 重建,期间转圈
 *
 * Vue 那边守卫是 `beforeEach` 每次导航返回重定向;React 这里是组件级:整个受保护区一个 `Protected`,
 * 它常驻挂载,内部 `useRoutes` 随 `menuTree` 变化重匹配。**没有 Vue 的 `return to.fullPath` 重解析**。
 *
 * chooser 态不需要单独的分支:`enter` 没被调 → `menuTree` 保持空 → 动态路由为空、`homePath` 回落
 * `/module` → `/module` 渲染选择器。空 `menuTree` + `homePath` 兜底天然表达了 chooser。
 */
export function Protected() {
  const loggedIn = useUserStore(isLoggedIn)
  const mustChange = useUserStore((s) => s.userInfo?.mustChangePassword ?? false)
  const routesReady = useAuthStore((s) => s.routesReady)
  const location = useLocation()
  // 「已尝试过引导」。routesReady 只在 enter() 成功时转真,而 chooser 态它永远是 false ——
  // 靠它做重跑判据会 enterInitial 打点风暴。所以另立一个只跑一次的本地标志。
  const [booted, setBooted] = useState(routesReady)
  // Level3:F5 后 access 仅内存已空,须先凭 Cookie 静默刷新再判未登录(否则会误踢 /login)。
  const [sessionChecked, setSessionChecked] = useState(() => {
    const s = useUserStore.getState()
    return !!s.accessToken || !isCookieSession(s)
  })

  useEffect(() => {
    if (sessionChecked) return
    let alive = true
    void tryRestoreCookieSession().finally(() => {
      if (alive) setSessionChecked(true)
    })
    return () => {
      alive = false
    }
  }, [sessionChecked])

  useEffect(() => {
    if (!sessionChecked || !loggedIn || mustChange || routesReady || booted) return
    let alive = true
    enterInitial()
      .catch(() => useUserStore.getState().clear()) // 拉门户失败 → 清会话,下面回落 /login
      .finally(() => {
        if (alive) setBooted(true)
      })
    return () => {
      alive = false
    }
  }, [sessionChecked, loggedIn, mustChange, routesReady, booted])

  if (!sessionChecked) {
    return (
      <div style={{ display: 'grid', placeItems: 'center', minHeight: '100vh' }}>
        <Spin size="large" />
      </div>
    )
  }
  if (!loggedIn) return <Navigate to="/login" replace />
  // 强制改密:只允许停在改密页。放在 routesReady 之前 —— 改密页是静态路由,不需要门户重建即可渲染,
  // 避免"重建→选应用→又被弹回"的循环。
  if (mustChange) {
    return location.pathname === '/personal/password' ? lazyEl(PasswordPage) : <Navigate to="/personal/password" replace />
  }
  if (!routesReady && !booted) {
    return (
      <div style={{ display: 'grid', placeItems: 'center', minHeight: '100vh' }}>
        <Spin size="large" />
      </div>
    )
  }
  return <DynamicRoutes />
}

/** routesReady(或 chooser 引导完)后的路由表:菜单页在**布局壳**内,选择页/门户级页在壳外(全屏)。 */
function DynamicRoutes() {
  const menuTree = useAuthStore((s) => s.menuTree)
  const home = useAuthStore(homePath)
  const routes = useMemo(
    () => [
      // 选择页全屏,不进布局壳(对齐 Vue:它不是 layout 的子路由)。静态段 `/module` 优先于壳内的 `*`
      // —— **react-router 按路由特异性排名裁决,与数组顺序无关**(实测挪到末位仍赢);写在最前只是可读性。
      { path: '/module', element: <ModuleChooser /> },
      // 布局壳:菜单派生的动态路由、个人页、根重定向、404 都在壳内,渲染进 Sider+Header 的内容区。
      {
        element: <LayoutShell />,
        children: [
          ...buildRoutes(menuTree),
          ...buildDetailRoutes(),
          // 个人中心五页(静态路由,壳内渲染;入口在顶栏用户下拉 / 铃铛)。
          { path: '/personal/profile', element: lazyEl(ProfilePage) },
          { path: '/personal/password', element: lazyEl(PasswordPage) },
          { path: '/personal/notice', element: lazyEl(NoticePage) },
          { path: '/personal/sessions', element: lazyEl(SessionsPage) },
          { path: '/personal/bindings', element: lazyEl(BindingsPage) },
          // '/' 落到当前应用首页;chooser 态 homePath 回落 /module。
          { path: '/', element: <Navigate to={home} replace /> },
          { path: '*', element: lazyEl(NotFoundPage) },
        ],
      },
    ],
    [menuTree, home],
  )
  return useRoutes(routes)
}
