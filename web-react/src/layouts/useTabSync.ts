import { useEffect } from 'react'
import { useLocation } from 'react-router-dom'
import { useAuthStore } from '@/stores/auth'
import { useTabsStore } from '@/stores/tabs'
import { isHttpUrl } from '@/utils/url'
import type { MenuNode } from '@/types/menu'

/** 个人中心静态页(不在 menuTree)→ i18n 标题键 + 图标。tabTitle 见 `.` 走 t()。 */
const PERSONAL_META: Record<string, { title: string; icon: string }> = {
  '/personal/profile': { title: 'menu.profile', icon: 'ph:user' },
  '/personal/password': { title: 'menu.password', icon: 'ph:key' },
  '/personal/notice': { title: 'menu.notice', icon: 'ph:bell' },
  '/personal/sessions': { title: 'menu.sessions', icon: 'ph:desktop' },
  '/personal/bindings': { title: 'menu.bindings', icon: 'ph:link-simple' },
}

/** menuTree 扁平化:path → {title(原始,可能是 i18n key), icon(iconify 名)}。外链跳过(不会成为 location)。 */
function flatten(tree: MenuNode[], out: Map<string, { title: string; icon?: string }>): Map<string, { title: string; icon?: string }> {
  for (const n of tree) {
    if (n.path && !isHttpUrl(n.path)) out.set(n.path, { title: n.title, icon: n.icon })
    if (n.children.length) flatten(n.children, out)
  }
  return out
}

/**
 * 路由 → 标签同步(替代 Vue `router.afterEach(addTab)`)。当前 pathname 变化时,查菜单/个人页元数据补一个标签。
 * 无标题来源(如 `/module` 在壳外、404)则不建标签。store 保持 router-free,导航侧(菜单点击、标签点击)自行 navigate。
 */
export function useTabSync() {
  const location = useLocation()
  const pathname = location.pathname
  const fullPath = location.pathname + location.search
  const menuTree = useAuthStore((s) => s.menuTree)

  useEffect(() => {
    const meta = flatten(menuTree, new Map()).get(pathname) ?? PERSONAL_META[pathname]
    if (!meta) return // 无标题来源 → 不建标签
    useTabsStore.getState().addTab({ path: pathname, fullPath, title: meta.title, icon: meta.icon, noCache: false })
  }, [pathname, fullPath, menuTree])
}
