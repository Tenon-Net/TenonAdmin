import type { Component } from 'vue'
import { router, resetRouter, registerDynamic } from '@/router'
import { useAuthStore } from '@/stores/auth'
import { personalApi } from '@/api'
import { MenuType, type MenuNode } from '@/types/menu'

// 所有页面组件:component 字符串 → 对应文件(如 "system/user/index" → /src/views/system/user/index.vue)。
const views = import.meta.glob('/src/views/**/*.vue') as Record<string, () => Promise<Component>>

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
      component: loader,
      meta: { title: node.title, icon: node.icon, keepAlive: true },
    })
    registerDynamic(name)
  }
  auth.routesReady = true
}
