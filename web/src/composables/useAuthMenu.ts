import type { Component } from 'vue'
import { router, resetRouter, registerDynamic } from '@/router'
import { namedPage } from '@/router/namedPage'
import { registerDetailRoutes } from '@/router/detailRoutes'
import { useAuthStore } from '@/stores/auth'
import { personalApi } from '@/api'
import { isHttpUrl } from '@/utils/url'
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
    if (node.type !== MenuType.Menu || !node.path) continue
    // 外链菜单:path 为 URL(component 空)→ 不建路由,点击时 window.open(见 useLayoutMenu / MenuSearch)。
    if (isHttpUrl(node.path)) continue

    const name = `menu-${node.id}`
    const routePath = node.path.startsWith('/') ? node.path : `/${node.path}`

    // 内嵌 iframe 菜单:component 为 URL → 注册通用 iframe 视图,URL 进 meta.iframeSrc(keep-alive 顺带保住 iframe 状态)。
    if (isHttpUrl(node.component)) {
      if (router.hasRoute(name)) router.removeRoute(name)
      router.addRoute('layout', {
        path: routePath,
        name,
        component: namedPage(name, () => import('@/views/embed/iframe.vue')),
        meta: { title: node.title, icon: node.icon, keepAlive: true, iframeSrc: node.component },
      })
      registerDynamic(name)
      continue
    }

    if (!node.component) continue
    const key = `/src/views/${node.component.replace(/^\/+/, '')}.vue`
    const loader = views[key]
    if (!loader) {
      // eslint-disable-next-line no-console
      console.warn('[menu] 缺少视图组件:', node.component, '→', key)
      continue
    }
    if (router.hasRoute(name)) router.removeRoute(name)
    router.addRoute('layout', {
      path: routePath,
      name,
      component: namedPage(name, loader),
      meta: { title: node.title, icon: node.icon, keepAlive: true },
    })
    registerDynamic(name)
  }
  // 约定式详情路由(views/**/detail.vue → /<路径>/:id/detail)随菜单路由一并注册,
  // 故 F5/深链走守卫重建时详情路由也复活(见 router/detailRoutes.ts)。
  registerDetailRoutes()
  auth.routesReady = true
}
