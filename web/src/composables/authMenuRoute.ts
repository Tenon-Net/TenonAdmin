import { isHttpUrl } from '@/utils/url'
import { MenuType, type MenuNode } from '@/types/menu'

export type MenuRouteDescriptor =
  | { kind: 'iframe'; path: string; name: string; title: string; icon?: string; iframeSrc: string }
  | { kind: 'view'; path: string; name: string; title: string; icon?: string; viewKey: string }
  | { kind: 'missing'; path: string; name: string; title: string; icon?: string; component: string }

/** 将门户菜单节点归类为动态路由描述符；无须注册路由的节点返回 null。 */
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
