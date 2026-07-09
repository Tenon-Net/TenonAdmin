import { defineStore } from 'pinia'
import type { AppModule, MenuNode } from '@/types/menu'
import { useTabsStore } from '@/stores/tabs'

/**
 * 授权态:模块 / 菜单树 / 权限码 / 路由是否已建。
 * **只持久化 currentModuleId 一项**——F5/深链时守卫据它重建"上次所在应用"的动态路由,
 * 跨应用深链(默认应用 ≠ 当前应用)才不会落 404。
 * menuTree/modules/routesReady 一律不持久化:动态路由只活在 router 内存里,刷新后由守卫重建;
 * 尤其 routesReady 若被持久化成 true,F5 会跳过重建导致每条动态路由 404。
 */
export const useAuthStore = defineStore('auth', {
  state: () => ({
    modules: [] as AppModule[],
    currentModuleId: null as number | null,
    menuTree: [] as MenuNode[],
    // 后端暂无"返回按钮权限码"接口;留空 → v-auth fail-open,服务端 403 兜底。
    permissionCodes: [] as string[],
    routesReady: false,
  }),
  actions: {
    reset() {
      this.modules = []
      this.currentModuleId = null
      this.menuTree = []
      this.permissionCodes = []
      this.routesReady = false
      useTabsStore().clearTabs() // 登出销毁授权态时一并清标签(reset 仅登出路径调用,故不会在 F5 误清)
    },
  },
  // 仅存 currentModuleId(见上方注释);routesReady/menuTree 绝不入库,否则 F5 跳过重建。
  persist: { pick: ['currentModuleId'] },
})
