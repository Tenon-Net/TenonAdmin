import { useEffect, useMemo, useState } from 'react'
import { Spin } from 'antd'
import { Navigate, useLocation, useRoutes } from 'react-router-dom'
import { useUserStore, isLoggedIn } from '@/stores/user'
import { useAuthStore, homePath } from '@/stores/auth'
import { enterInitial } from '@/composables/useModule'
import { buildRoutes } from './buildRoutes'
import UnderConstruction from '@/views/_placeholder/UnderConstruction'

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

  useEffect(() => {
    if (!loggedIn || mustChange || routesReady || booted) return
    let alive = true
    enterInitial()
      .catch(() => useUserStore.getState().clear()) // 拉门户失败 → 清会话,下面回落 /login
      .finally(() => {
        if (alive) setBooted(true)
      })
    return () => {
      alive = false
    }
  }, [loggedIn, mustChange, routesReady, booted])

  if (!loggedIn) return <Navigate to="/login" replace />
  // 强制改密:只允许停在改密页。放在 routesReady 之前 —— 改密页是静态路由,不需要门户重建即可渲染,
  // 避免"重建→选应用→又被弹回"的循环。
  if (mustChange) {
    return location.pathname === '/personal/password' ? <UnderConstruction /> : <Navigate to="/personal/password" replace />
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

/** routesReady(或 chooser 引导完)后的路由表:菜单派生的动态路由 + 几条静态路由。 */
function DynamicRoutes() {
  const menuTree = useAuthStore((s) => s.menuTree)
  const home = useAuthStore(homePath)
  const routes = useMemo(
    () => [
      ...buildRoutes(menuTree),
      { path: '/module', element: <UnderConstruction /> }, // B7 换真选择器
      { path: '/personal/password', element: <UnderConstruction /> },
      // '/' 落到当前应用首页;chooser 态 homePath 回落 /module。
      { path: '/', element: <Navigate to={home} replace /> },
      { path: '*', element: <NotFound /> },
    ],
    [menuTree, home],
  )
  return useRoutes(routes)
}

function NotFound() {
  const { pathname } = useLocation()
  return (
    <div style={{ display: 'grid', placeItems: 'center', minHeight: 320, padding: 24 }}>
      <span>404 —— 没有这个页面:{pathname}</span>
    </div>
  )
}
