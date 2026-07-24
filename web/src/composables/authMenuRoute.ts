import { isHttpUrl } from '@/utils/url'
import { MenuType, type MenuNode } from '@/types/menu'

/**
 * 菜单节点的可物化结果。三种变体把 iframe/view/missing 各自所需的数据收窄，调用方无需再次
 * 解释后端的 `component` 字符串。
 */
export type MenuRouteDescriptor =
  | { kind: 'iframe'; path: string; name: string; title: string; icon?: string; iframeSrc: string }
  | { kind: 'view'; path: string; name: string; title: string; icon?: string; viewKey: string }
  | { kind: 'missing'; path: string; name: string; title: string; icon?: string; component: string }

/**
 * 只负责菜单路由决策，让分支逻辑能脱离 vue-router 做单测；路由注册与组件物化仍由
 * `useAuthMenu` 负责。`viewKeys` 必须来自同一张页面 glob，避免存在性判断与 loader 来源漂移。
 */
export function describeMenuRoute(node: MenuNode, viewKeys: ReadonlySet<string>): MenuRouteDescriptor | null {
  if (node.type !== MenuType.Menu || !node.path || isHttpUrl(node.path)) return null

  const path = node.path.startsWith('/') ? node.path : `/${node.path}`
  const name = `menu-${node.id}`
  const component = node.component
  if (component && isHttpUrl(component)) {
    return { kind: 'iframe', path, name, title: node.title, icon: node.icon, iframeSrc: component }
  }
  if (!component) return null

  const viewKey = `/src/views/${component.replace(/^\/+/, '')}.vue`
  if (viewKeys.has(viewKey)) return { kind: 'view', path, name, title: node.title, icon: node.icon, viewKey }
  return { kind: 'missing', path, name, title: node.title, icon: node.icon, component }
}
