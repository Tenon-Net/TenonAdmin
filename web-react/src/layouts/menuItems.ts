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

/**
 * 当前路径命中的叶子,其所有祖先目录 key(`cat-${id}`)—— 喂给 antd `Menu` 的 `openKeys`,
 * 让选中项所在的子菜单**跟着路由自动展开**(inline 模式下否则可能是收起的、看不到选中项)。
 * 纯函数,便于单测。命中不了返回空数组(off-menu 路由如 /personal/*,不强行展开谁)。
 *
 * **同一 path 挂在多个目录下时,只展开第一处命中的祖先链**(`for` 首个命中即 return)。真实菜单不会把
 * 同一路由挂两处,故不为这个不会发生的情形收集全部分支;真遇到了,展开与高亮(两处都亮)会不一致。
 */
export function openKeysFor(items: MenuItem[], path: string): string[] {
  for (const it of items) {
    if (!it.children?.length) continue
    // 直接子项命中,或更深处命中 → 这条目录要展开,连同它下面命中的更深目录。
    const deeper = openKeysFor(it.children, path)
    const hitHere = it.children.some((c) => c.key === path)
    if (hitHere || deeper.length) return [it.key, ...deeper]
  }
  return []
}

// ── 多布局模式(D2a)的一/二级派生。对齐 Vue `useLayoutMenu` 的 l1Options/l2Options/findActiveL1/firstLeafKey。 ──

/** 一级项:剥掉 children,渲染为可选的普通项(rail / 顶栏 horizontal 的一级菜单)。 */
export function l1Of(items: MenuItem[]): MenuItem[] {
  return items.map(({ key, label, icon }) => ({ key, label, icon }))
}

/** 选中一级下的二级子树(混合布局的侧栏/顶栏 L2)。选不中或无子项 → 空数组。 */
export function l2Of(items: MenuItem[], l1Key: string | undefined): MenuItem[] {
  return items.find((i) => i.key === l1Key)?.children ?? []
}

/** path 命中的叶子所属的一级项 key。命中不了返回 undefined(off-menu 路由如 /personal/*)。 */
export function findActiveL1(items: MenuItem[], path: string): string | undefined {
  const contains = (it: MenuItem): boolean => it.key === path || (it.children ?? []).some(contains)
  return items.find(contains)?.key
}

/** 一级项下第一个内部叶子(key 以 '/' 开头)——选一级时的导航目标。无内部叶子返回 undefined。 */
export function firstLeafKey(item: MenuItem): string | undefined {
  if (item.key.startsWith('/')) return item.key
  for (const c of item.children ?? []) {
    const k = firstLeafKey(c)
    if (k) return k
  }
  return undefined
}

/**
 * path 命中的叶子,从根到它的标题链(面包屑)。MenuItem 的 label 已翻译,故直接产出可显示文案。
 * 命中不了返回空数组(off-menu 路由如 /personal/*,不显示面包屑)。
 */
export function breadcrumbFor(items: MenuItem[], path: string): string[] {
  for (const it of items) {
    if (it.key === path) return [it.label]
    const deeper = breadcrumbFor(it.children ?? [], path)
    if (deeper.length) return [it.label, ...deeper]
  }
  return []
}

/** 命令面板全局搜索用的叶子:key(内部 path 或外链 URL)+ 已翻译 label + 图标 + 从根到叶的 label 链(含自身)。 */
export interface MenuLeaf {
  key: string
  label: string
  icon?: ReactNode
  breadcrumb: string[]
}

/**
 * 全部叶子(页面 / 外链)+ 面包屑链,供命令面板搜索。目录不入(只递归)。对齐 Vue `useMenuFlat`,
 * 但 MenuItem 的 label 已翻译、Button/隐藏/排序已在 `menuToItems` 处理,故这里只需按有无 children 分叶子/目录。
 */
export function flatLeaves(items: MenuItem[], trail: string[] = []): MenuLeaf[] {
  const out: MenuLeaf[] = []
  for (const it of items) {
    const here = [...trail, it.label]
    if (it.children?.length) out.push(...flatLeaves(it.children, here))
    else out.push({ key: it.key, label: it.label, icon: it.icon, breadcrumb: here })
  }
  return out
}
