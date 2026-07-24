import type { Component } from 'vue'
import { router, resetRouter, registerDynamic } from '@/router'
import { namedPage } from '@/router/namedPage'
import { registerDetailRoutes } from '@/router/detailRoutes'
import { useAuthStore } from '@/stores/auth'
import { personalApi } from '@/api'
import { describeMenuRoute } from './authMenuRoute'
import type { MenuNode } from '@/types/menu'

// 所有页面组件:component 字符串 → 对应文件(如 "system/user/index" → /src/views/system/user/index.vue)。
const views = import.meta.glob('/src/views/**/*.vue') as Record<string, () => Promise<Component>>
const viewKeys = new Set(Object.keys(views))

/**
 * 菜单 component 字段的全部合法取值(由上面这张 glob 表反推,天然不会漂移)。
 * 供菜单管理表单的「组件路径」下拉:手敲错一个字符时,下面 buildRoutesForModule 会保留路径并落到
 * MissingRoute 诊断页;下拉仍是第一道防线,让配置值从同一张 glob 表产生。
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
    const route = describeMenuRoute(node, viewKeys)
    if (!route) continue

    if (router.hasRoute(route.name)) router.removeRoute(route.name)
    if (route.kind === 'iframe') {
      router.addRoute('layout', {
        path: route.path,
        name: route.name,
        component: namedPage(route.name, () => import('@/views/embed/iframe.vue')),
        meta: { title: route.title, icon: route.icon, keepAlive: true, iframeSrc: route.iframeSrc },
      })
      registerDynamic(route.name)
      continue
    }

    if (route.kind === 'missing') {
      // eslint-disable-next-line no-console
      console.warn('[menu] 缺少视图组件:', route.component)
      router.addRoute('layout', {
        path: route.path,
        name: route.name,
        component: namedPage(route.name, () => import('@/views/error/MissingRoute.vue')),
        meta: { title: route.title, icon: route.icon, keepAlive: true, missingComponent: route.component },
      })
      registerDynamic(route.name)
      continue
    }

    const loader = views[route.viewKey]
    router.addRoute('layout', {
      path: route.path,
      name: route.name,
      component: namedPage(route.name, loader),
      meta: { title: route.title, icon: route.icon, keepAlive: true },
    })
    registerDynamic(route.name)
  }
  // 约定式详情路由(views/**/detail.vue → /<路径>/:id/detail)随菜单路由一并注册,
  // 故 F5/深链走守卫重建时详情路由也复活(见 router/detailRoutes.ts)。
  registerDetailRoutes()
  auth.routesReady = true
}
