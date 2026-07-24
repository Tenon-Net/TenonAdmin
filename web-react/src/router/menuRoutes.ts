import { isHttpUrl } from '@/utils/url'
import { MenuType, type MenuNode } from '@/types/menu'

/**
 * 菜单树 → 路由描述符。这是「动态路由」里**唯一有真实分支逻辑**的一块,所以从 react-router 的
 * materialization(见 `buildRoutes.tsx`)里剥出来单独测:决策(哪些节点建路由、建成什么)在这里,
 * React.lazy / Suspense / RouteObject 的拼装在那里。规则逐条对齐 Vue 侧 `useAuthMenu.buildRoutesForModule`。
 *
 * `hasView` 与 `warn` 注入,不在函数里直接摸 `import.meta.glob` / `console`:
 * 前者让「组件存不存在」可控,后者让「缺组件要告警」可断言 —— 否则那条 warn 分支永远测不到。
 */
interface RouteDescriptorBase {
  /** 路由名,`menu-${id}`。keep-alive / 精确移除都按它。 */
  name: string
  /** 归一后的路由路径(前导斜杠)。 */
  path: string
  /** 视图组件键(如 `system/user/index`);iframe 描述符没有它。 */
  /** component 为 http URL 时,内嵌 iframe 的目标地址。 */
  title: string
  icon?: string
}

export type RouteDescriptor =
  | (RouteDescriptorBase & { kind: 'view'; component: string })
  | (RouteDescriptorBase & { kind: 'iframe'; iframeSrc: string })
  | (RouteDescriptorBase & { kind: 'missing'; component: string })

function flatten(nodes: MenuNode[]): MenuNode[] {
  return nodes.flatMap((n) => [n, ...(n.children?.length ? flatten(n.children) : [])])
}

/**
 * @param hasView 判定某个 component 键是否有对应视图文件(真实调用方传 `(k) => k in views` 的 glob 表)。
 * @param warn    组件缺失时的控制台告警(默认 `console.warn`;页面内另由 MissingRoute 给出可见诊断)。
 */
export function menuToRouteDescriptors(
  tree: MenuNode[],
  hasView: (component: string) => boolean,
  warn: (message: string, component: string, key: string) => void = (m, c, k) =>
    // eslint-disable-next-line no-console
    console.warn(m, c, k),
): RouteDescriptor[] {
  const out: RouteDescriptor[] = []
  for (const node of flatten(tree)) {
    // 目录(Catalog)只组织层级、自己不落地成页;无 path 的 Menu 同理无处可去。
    if (node.type !== MenuType.Menu || !node.path) continue
    // 外链菜单:path 为 URL(component 空)→ 不建路由,点击时 window.open(布局侧菜单处理)。
    if (isHttpUrl(node.path)) continue

    const name = `menu-${node.id}`
    const path = node.path.startsWith('/') ? node.path : `/${node.path}`

    // 内嵌 iframe 菜单:component 为 URL(path 为内部路径)→ 通用 iframe 视图,URL 进 iframeSrc。
    if (isHttpUrl(node.component)) {
      out.push({ name, path, kind: 'iframe', iframeSrc: node.component!, title: node.title, icon: node.icon })
      continue
    }

    if (!node.component) continue
    const key = node.component.replace(/^\/+/, '')
    if (!hasView(key)) {
      // 保留原路径交给 MissingRoute,同时给开发者控制台留一条可搜索的诊断。
      warn('[menu] 缺少视图组件:', node.component, key)
      out.push({ name, path, kind: 'missing', component: key, title: node.title, icon: node.icon })
      continue
    }
    out.push({ name, path, kind: 'view', component: key, title: node.title, icon: node.icon })
  }
  return out
}
