import type { ReactNode } from 'react'
import { isHttpUrl } from '@/utils/url'
import { MenuType, type MenuNode } from '@/types/menu'

/**
 * 菜单树 → antd `Menu` 的 items。规则对齐 Vue 侧 `useLayoutMenu.toOptions`:
 * 按 `sort` 升序 → 剥掉 Button 与 `visible===false` → 目录(Catalog)递归、**无可见子项则整条丢弃** →
 * 页面叶子 `key = path`(选中态与导航都靠它;缺 path 兜底 `menu-${id}`)→ 标题含 `.` 走 i18n。
 *
 * **外链菜单(path 为 URL)照常入菜单**,`key` 就是那个 URL,点击时 `window.open`(见 `useLayoutMenu`)——
 * 这与 `buildRoutes` 把外链**跳过不建路由**是**相反**的:外链要能在菜单里看到、点得动,只是不占一条内部路由。
 *
 * `tr`(标题 i18n)与 `iconFor`(图标渲染)注入 —— 让这份纯逻辑不摸 i18n 单例 / React 组件,可单测。
 */
export interface MenuItem {
  key: string
  label: string
  icon?: ReactNode
  children?: MenuItem[]
}

const trTitle = (s: string, tr: (k: string) => string) => (s.includes('.') ? tr(s) : s)

export function menuToItems(
  nodes: MenuNode[],
  tr: (k: string) => string,
  iconFor: (name: string | undefined, isCatalog: boolean) => ReactNode,
): MenuItem[] {
  return [...nodes]
    .sort((a, b) => a.sort - b.sort)
    .filter((n) => n.type !== MenuType.Button && n.visible !== false)
    .map<MenuItem | null>((n) => {
      if (n.type === MenuType.Catalog) {
        const children = menuToItems(n.children ?? [], tr, iconFor)
        if (children.length === 0) return null // 无可见子项的空目录不出现
        return { key: `cat-${n.id}`, label: trTitle(n.title, tr), icon: iconFor(n.icon, true), children }
      }
      // 页面叶子 / 外链叶子:key 是 path(外链则是 URL)。
      return { key: n.path ?? `menu-${n.id}`, label: trTitle(n.title, tr), icon: iconFor(n.icon, false) }
    })
    .filter((o): o is MenuItem => o !== null)
}

/** 菜单点击:外链 → 新标签打开(返回已处理);内部路径由调用方 navigate。 */
export function openIfExternal(key: string): boolean {
  if (!isHttpUrl(key)) return false
  window.open(key, '_blank', 'noopener,noreferrer')
  return true
}
