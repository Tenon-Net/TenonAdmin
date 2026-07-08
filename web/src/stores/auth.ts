import { defineStore } from 'pinia'
import type { AppModule, MenuNode } from '@/types/menu'

/**
 * 授权态:模块 / 菜单树 / 权限码 / 路由是否已建。
 * **不持久化**——动态路由只活在 router 内存里,刷新后由守卫重建;
 * 若持久化 routesReady=true,F5 会跳过重建导致每条动态路由 404。
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
    },
  },
})
