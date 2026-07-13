import type { Component } from 'vue'
import { router, resetRouter, registerDynamic } from '@/router'
import { namedPage } from '@/router/namedPage'
import { useAuthStore } from '@/stores/auth'
import { personalApi } from '@/api'
import { MenuType, type MenuNode } from '@/types/menu'

// 所有页面组件:component 字符串 → 对应文件(如 "system/user/index" → /src/views/system/user/index.vue)。
const views = import.meta.glob('/src/views/**/*.vue') as Record<string, () => Promise<Component>>

/**
 * 菜单 component 字段的全部合法取值(由上面这张 glob 表反推,天然不会漂移)。
 * 供菜单管理表单的「组件路径」下拉:手敲错一个字符,下面 buildRoutesForModule 只会 console.warn 然后跳过,
 * 表现是这个菜单项<b>静默消失</b>——管理员根本不知道自己错在哪。给个下拉就从根上没了这个坑。
 */
export const viewComponentPaths: string[] = Object.keys(views)
  .map((k) => k.replace(/^\/src\/views\//, '').replace(/\.vue$/, ''))
  .sort()

function flatten(nodes: MenuNode[]): MenuNode[] {
  return nodes.flatMap((n) => [n, ...(n.children?.length ? flatten(n.children) : [])])
}

/** 拉某应用的菜单树 → 重建动态路由(挂在 layout 下)。 */
export async function buildRoutesForModule(moduleId: number): Promise<void> {
  const auth = useAuthStore()
  const tree = await personalApi.menu(moduleId)
  auth.menuTree = tree
  auth.currentModuleId = moduleId

  resetRouter()
  for (const node of flatten(tree)) {
    if (node.type !== MenuType.Menu || !node.component || !node.path) continue
    const key = `/src/views/${node.component.replace(/^\/+/, '')}.vue`
    const loader = views[key]
    if (!loader) {
      // eslint-disable-next-line no-console
      console.warn('[menu] 缺少视图组件:', node.component, '→', key)
      continue
    }
    const name = `menu-${node.id}`
    if (router.hasRoute(name)) router.removeRoute(name)
    router.addRoute('layout', {
      path: node.path.startsWith('/') ? node.path : `/${node.path}`,
      name,
      component: namedPage(name, loader),
      meta: { title: node.title, icon: node.icon, keepAlive: true },
    })
    registerDynamic(name)
  }
  auth.routesReady = true
}
