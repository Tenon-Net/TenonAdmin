// 菜单页纯逻辑:默认表单 / 全量映射 / 剥按钮 / 按钮信息 / 子树 id / 路由应用软过滤 / 方法默认标题。
// 抽出来做变异钉——index.tsx 与 ButtonManager.tsx 只接线。对齐 Vue 侧 menu/index.vue + ButtonManager.vue。
import { MenuType, type MenuInput, type MenuTreeNode } from '@/types/menu'

/** 「未分配」哨兵:雪花 id 无 0,用它表示 moduleId==null 的顶级目录分组。 */
export const UNASSIGNED = 0

/** 「全部」哨兵:雪花 id 无负数,用它表示不按应用过滤。默认筛选停在当前应用时,
 * 别的应用底下新建的菜单会被筛掉、看着像"存了但不见了"(issue #17)——需要一个能看全量的退路。 */
export const ALL_MODULES = -1

/** 新增默认表单。type 默认页面(主表单),按钮弹窗传 MenuType.Button;moduleId 仅顶级目录有效。 */
export function blankMenu(parentId = 0, moduleId: number | null = null, type: MenuType = MenuType.Menu): MenuInput {
  return { parentId, type, title: '', permission: '', sort: 0, enabled: true, moduleId, path: '', component: '', icon: '', visible: true }
}

/**
 * 行 → 全量入参:openEdit 回填与 StatusSwitch 行内改状态**共用**——后端无独立启停端点,均走全量 update,
 * 漏一字段就把该字段抹空,故逐字段带全(可空字段归一)。按钮场景 moduleId 恒 null(按钮无所属模块)。变异钉。
 */
export function menuRowToInput(r: MenuTreeNode): MenuInput {
  return {
    parentId: r.parentId, type: r.type, title: r.title, permission: r.permission, sort: r.sort,
    enabled: r.enabled, moduleId: r.moduleId ?? null,
    path: r.path ?? '', component: r.component ?? '', icon: r.icon ?? '', visible: r.visible,
  }
}

/** 递归剔除按钮节点——按钮改由 ButtonManager 单独管,不进主树,树只剩目录/菜单。 */
export function stripButtons(nodes: MenuTreeNode[]): MenuTreeNode[] {
  return nodes.filter((n) => n.type !== MenuType.Button).map((n) => ({ ...n, children: stripButtons(n.children) }))
}

/**
 * 各节点**直属**按钮的数量 + 权限码拼串(取自未剥离的原树)。
 * count 给「配置权限」列徽标;perms 给关键字搜索——按钮已不在树里,「这个权限码归哪个页面」
 * 只能靠父页面命中来答,否则藏起按钮就等于把它搜没了。
 */
export function buildButtonInfo(tree: MenuTreeNode[]): Map<number, { count: number; perms: string }> {
  const m = new Map<number, { count: number; perms: string }>()
  const walk = (nodes: MenuTreeNode[]) => {
    for (const n of nodes) {
      const btns = n.children.filter((c) => c.type === MenuType.Button)
      m.set(n.id, { count: btns.length, perms: btns.map((b) => b.permission).join(' ').toLowerCase() })
      walk(n.children)
    }
  }
  walk(tree)
  return m
}

/** 收集以 id 为根的子树全部 id(含自身)——编辑时排除,防把节点挂到自己后代下成环。 */
export function subtreeIds(nodes: MenuTreeNode[], id: number): Set<number> {
  const out = new Set<number>()
  const find = (list: MenuTreeNode[]): MenuTreeNode | null => {
    for (const n of list) {
      if (n.id === id) return n
      const hit = find(n.children)
      if (hit) return hit
    }
    return null
  }
  const collect = (n: MenuTreeNode) => { out.add(n.id); n.children.forEach(collect) }
  const root = find(nodes)
  if (root) collect(root)
  return out
}

/** appPrefix → 规整路由段(去首尾斜杠、去空白)。空段=不按应用过滤。 */
export function routeSeg(appPrefix?: string): string {
  return (appPrefix ?? '').trim().replace(/^\/+|\/+$/g, '')
}

/** 路由是否属当前应用:段为空→全属;否则 path 含 `/seg/` 或以 `/seg` 结尾。 */
export function belongsToApp(path: string, seg: string): boolean {
  if (!seg) return true
  return path.includes(`/${seg}/`) || path.endsWith(`/${seg}`)
}

/** HTTP 方法 → 默认标题的 i18n 键;未知方法返回 null(调用方回落原方法串)。变异钉。 */
export function defaultTitleKey(method: string): string | null {
  const m = method.toUpperCase()
  if (m === 'GET') return 'menu.btnQuery'
  if (m === 'POST') return 'menu.btnCreate'
  if (m === 'PUT' || m === 'PATCH') return 'menu.btnUpdate'
  if (m === 'DELETE') return 'menu.btnDelete'
  return null
}
