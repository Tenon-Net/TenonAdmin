// 授权菜单三态勾选的纯逻辑:构树成组 + 三态重算 + 目录/菜单/按钮的不可变切换 + 收集选中 id + 过滤。
// 抽出做变异钉——GrantMenuTable.tsx 只做状态与渲染接线。对齐 Vue 侧 GrantMenuTable.vue(那边就地改 reactive,这里返新对象)。
import { MenuType, type MenuTreeNode } from '@/types/menu'

/** 「未分配」哨兵:雪花 id 无 0,用它表示 moduleId==null 的顶级目录分组。 */
export const UNASSIGNED = 0

export interface ButtonItem { id: number; title: string; checked: boolean }
export interface MenuRow { id: number; title: string; checked: boolean; buttons: ButtonItem[] }
/** 目录组:moduleId 取自顶级节点(过滤用);checked/indeterminate 由其下菜单+按钮三态派生。 */
export interface CatalogGroup { id: number; title: string; moduleId: number | null; checked: boolean; indeterminate: boolean; menus: MenuRow[] }

function toMenuRow(m: MenuTreeNode, granted: Set<number>): MenuRow {
  return {
    id: m.id, title: m.title, checked: granted.has(m.id),
    buttons: m.children.filter((b) => b.type === MenuType.Button).map((b) => ({ id: b.id, title: b.title, checked: granted.has(b.id) })),
  }
}

/** 目录三态:全选=有勾且全勾;半选=有勾但没全勾(以「菜单 + 全部按钮」为总集)。 */
export function groupState(menus: MenuRow[]): { checked: boolean; indeterminate: boolean } {
  const all = menus.flatMap((m) => [m as { checked: boolean }, ...m.buttons])
  const n = all.filter((x) => x.checked).length
  return { checked: n > 0 && n === all.length, indeterminate: n > 0 && n < all.length }
}

/** 树 → 目录组。顶级 Menu(如工作台)自身即菜单行(无目录壳);顶级 Catalog 取其 Menu 子为行。仅收 Catalog/Menu 顶级。 */
export function buildGroups(tree: MenuTreeNode[], granted: number[]): CatalogGroup[] {
  const set = new Set(granted)
  return tree
    .filter((n) => n.type === MenuType.Catalog || n.type === MenuType.Menu)
    .map((node) => {
      const menus = node.type === MenuType.Menu
        ? [toMenuRow(node, set)]
        : node.children.filter((m) => m.type === MenuType.Menu).map((m) => toMenuRow(m, set))
      return { id: node.id, title: node.title, moduleId: node.moduleId ?? null, menus, ...groupState(menus) }
    })
}

/** 切目录:其下菜单 + 全部按钮一并置 val。返回新组。 */
export function withCatalogChecked(group: CatalogGroup, val: boolean): CatalogGroup {
  const menus = group.menus.map((m) => ({ ...m, checked: val, buttons: m.buttons.map((b) => ({ ...b, checked: val })) }))
  return { ...group, checked: val, indeterminate: false, menus }
}

/** 切菜单:该菜单 + 其按钮一并置 val;目录三态重算。返回新组。 */
export function withMenuChecked(group: CatalogGroup, menuId: number, val: boolean): CatalogGroup {
  const menus = group.menus.map((m) => (m.id === menuId ? { ...m, checked: val, buttons: m.buttons.map((b) => ({ ...b, checked: val })) } : m))
  return { ...group, menus, ...groupState(menus) }
}

/**
 * 切单个按钮:置该按钮 val;若该菜单按钮**全勾**则连带把菜单也勾上(对齐 Vue:只在全勾时置 true,取消按钮不取消菜单)。
 * 目录三态重算。返回新组。
 */
export function withButtonChecked(group: CatalogGroup, menuId: number, btnId: number, val: boolean): CatalogGroup {
  const menus = group.menus.map((m) => {
    if (m.id !== menuId) return m
    const buttons = m.buttons.map((b) => (b.id === btnId ? { ...b, checked: val } : b))
    const allChecked = buttons.length > 0 && buttons.every((b) => b.checked)
    return { ...m, checked: allChecked ? true : m.checked, buttons }
  })
  return { ...group, menus, ...groupState(menus) }
}

/** 收集选中的全部 id:目录(勾或半勾)+ 菜单(勾)+ 按钮(勾)。全量替换角色授权用。 */
export function collectChecked(groups: CatalogGroup[]): number[] {
  const ids: number[] = []
  for (const g of groups) {
    if (g.checked || g.indeterminate) ids.push(g.id)
    for (const m of g.menus) {
      if (m.checked) ids.push(m.id)
      for (const b of m.buttons) if (b.checked) ids.push(b.id)
    }
  }
  return ids
}

/** 按所属应用过滤(moduleId 只在顶级);UNASSIGNED 归拢 moduleId==null。 */
export function filterByModule(groups: CatalogGroup[], moduleId: number): CatalogGroup[] {
  return groups.filter((g) => (moduleId === UNASSIGNED ? g.moduleId == null : g.moduleId === moduleId))
}

/** 关键字过滤:目录名命中→整组留;否则留标题/按钮命中的菜单;无命中菜单则丢该组。 */
export function filterBySearch(groups: CatalogGroup[], keyword: string): CatalogGroup[] {
  const q = keyword.trim().toLowerCase()
  if (!q) return groups
  const out: CatalogGroup[] = []
  for (const g of groups) {
    if (g.title.toLowerCase().includes(q)) { out.push(g); continue }
    const menus = g.menus.filter((m) => m.title.toLowerCase().includes(q) || m.buttons.some((b) => b.title.toLowerCase().includes(q)))
    if (menus.length) out.push({ ...g, menus })
  }
  return out
}
