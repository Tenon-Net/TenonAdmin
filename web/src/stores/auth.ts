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
    // 用户的默认应用(后端 SysUser.DefaultModuleId),随 enterInitial 拉取;不持久化,仅供选择页标记。
    defaultModuleId: null as number | null,
    menuTree: [] as MenuNode[],
    // 登录进门户时由 useModule.enterInitial 填入(GET /personal/permissions);超管为空集 → v-auth fail-open,服务端 sadm 兜底。
    permissionCodes: [] as string[],
    routesReady: false,
  }),
  getters: {
    /**
     * 当前应用的首页:模块自己的 DefaultRoute → 菜单树第一个叶子 → 应用选择器兜底。
     * 每个应用一个首页,所以 '/' 的落点不能写死(由守卫在路由就绪后取用,见 router/index.ts)。
     * 兜底是 /module 而非某个固定页:一个菜单都没配的应用没有首页可言,把人送回选择器,
     * 好过让他撞上一个"不属于本应用"的路径吃 404。
     */
    homePath(state): string {
      const m = state.modules.find((x) => x.id === state.currentModuleId)
      return m?.defaultRoute || firstLeafPath(state.menuTree) || '/module'
    },
  },
  actions: {
    reset() {
      this.modules = []
      this.currentModuleId = null
      this.defaultModuleId = null
      this.menuTree = []
      this.permissionCodes = []
      this.routesReady = false
      useTabsStore().clearTabs() // 登出销毁授权态时一并清标签(reset 仅登出路径调用,故不会在 F5 误清)
    },
  },
  // 仅存 currentModuleId(见上方注释);routesReady/menuTree 绝不入库,否则 F5 跳过重建。
  persist: { pick: ['currentModuleId'] },
})
