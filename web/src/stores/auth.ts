import { defineStore } from 'pinia'
import type { AppModule, MenuNode } from '@/types/menu'
import { useTabsStore } from '@/stores/tabs'

function firstLeafPath(tree: MenuNode[]): string | undefined {
  for (const n of tree) {
    if (n.path && !n.children?.length) return n.path
    const deeper = n.children?.length ? firstLeafPath(n.children) : undefined
    if (deeper) return deeper
  }
  return undefined
}

// 只持久化 currentModuleId；动态路由刷新后必须由守卫重建，routesReady 不能跨刷新保留。
export const useAuthStore = defineStore('auth', {
  state: () => ({
    modules: [] as AppModule[],
    currentModuleId: null as number | null,
    // 后端配置的默认应用，仅用于门户选路和选择页标记。
    defaultModuleId: null as number | null,
    menuTree: [] as MenuNode[],
    // 空集不代表超管；超管身份由 isSuperAdmin 单独判定。
    permissionCodes: [] as string[],
    // 普通用户未成功加载权限时隐藏受控按钮；刷新后由守卫重新拉取。
    permissionsLoaded: false,
    // 超级管理员的按钮权限不受权限码集合限制。
    isSuperAdmin: false,
    routesReady: false,
  }),
  getters: {
    // 无可用首页时回到应用选择器，避免跳进其他应用的固定路径。
    homePath(state): string {
      const m = state.modules.find((x) => x.id === state.currentModuleId)
      return m?.defaultRoute || firstLeafPath(state.menuTree) || '/module'
    },
    // v-auth 与渲染函数共用：超管放行，未加载时拒绝，否则精确匹配权限码。
    hasPerm(state): (code: string) => boolean {
      return (code) => (state.isSuperAdmin ? true : state.permissionsLoaded && state.permissionCodes.includes(code))
    },
  },
  actions: {
    reset() {
      this.modules = []
      this.currentModuleId = null
      this.defaultModuleId = null
      this.menuTree = []
      this.permissionCodes = []
      this.permissionsLoaded = false
      this.isSuperAdmin = false
      this.routesReady = false
      useTabsStore().clearTabs()
    },
  },
  persist: { pick: ['currentModuleId'] },
})
