import { defineStore } from 'pinia'
import type { RouteLocationNormalized } from 'vue-router'
import { router } from '@/router'
import { useAuthStore } from '@/stores/auth'

export interface TabItem {
  path: string // 主键(与 keep-alive 单实例一致)
  fullPath: string // 最后一次访问的完整 URL(含 query),点击标签时导航到此
  title: string // 原始 meta.title(可能是 i18n key)
  name: string // 路由名 → keep-alive include
  icon?: string
  affix?: boolean // 固定标签(= 当前应用的首页),不可关闭
}

/**
 * 多标签页会话:标签列表 + keep-alive 缓存名。sessionStorage 持久化(F5 恢复,新标签页从净态开始)。
 * 首页标签不写死:每个应用首页不同(homePath()),故 affix 在 addTab 时按当前应用判定。
 */
export const useTabsStore = defineStore('tabs', {
  state: () => ({
    tabs: [] as TabItem[],
    reloadKey: 0, // 变更 → 强制页面重挂(refreshTab)
    excludeName: '', // 临时逐出 keep-alive 实例(refreshTab)
  }),
  getters: {
    // 只保留已注册路由名(F5 重建期间剪除失效名),供 keep-alive :include。
    cachedNames(state): string[] {
      return state.tabs.map((t) => t.name).filter((n) => router.hasRoute(n))
    },
  },
  actions: {
    addTab(route: RouteLocationNormalized) {
      const name = String(route.name ?? '')
      if (!name) return
      const path = route.path
      const title = (route.meta.title as string) || name || path
      const icon = route.meta.icon as string | undefined
      const existing = this.tabs.find((t) => t.path === path)
      if (existing) {
        existing.fullPath = route.fullPath
        existing.title = title
        if (icon) existing.icon = icon
      } else {
        // 当前应用的首页 → 固定标签,不可关闭(每个应用各有一个)
        this.tabs.push({ path, fullPath: route.fullPath, title, name, icon, affix: path === useAuthStore().homePath })
      }
    },
    removeTab(path: string) {
      const idx = this.tabs.findIndex((t) => t.path === path)
      if (idx === -1 || this.tabs[idx]!.affix) return
      const wasActive = router.currentRoute.value.path === path
      this.tabs.splice(idx, 1)
      if (wasActive) {
        const next = this.tabs[idx] ?? this.tabs[idx - 1] ?? this.tabs[0]!
        router.push(next.fullPath)
      }
    },
    closeOthers(path: string) {
      this.tabs = this.tabs.filter((t) => t.affix || t.path === path)
      this._ensureActive(path)
    },
    closeAll() {
      this.tabs = this.tabs.filter((t) => t.affix)
      this._ensureActive(useAuthStore().homePath)
    },
    closeLeft(path: string) {
      const idx = this.tabs.findIndex((t) => t.path === path)
      if (idx <= 0) return
      this.tabs = this.tabs.filter((t, i) => t.affix || i >= idx)
      this._ensureActive(path)
    },
    closeRight(path: string) {
      const idx = this.tabs.findIndex((t) => t.path === path)
      if (idx === -1) return
      this.tabs = this.tabs.filter((t, i) => t.affix || i <= idx)
      this._ensureActive(path)
    },
    // 当前激活标签被批量关闭后,导航到 preferPath(通常是右键的那个)或末尾标签。
    _ensureActive(preferPath: string) {
      const cur = router.currentRoute.value.path
      if (this.tabs.some((t) => t.path === cur)) return
      const target = this.tabs.find((t) => t.path === preferPath) ?? this.tabs[this.tabs.length - 1]
      if (target) router.push(target.fullPath)
    },
    // 逐出缓存实例并发信号;default.vue 监听 reloadKey 做 v-if 重挂,重挂后清 excludeName。
    refreshTab(name: string) {
      this.excludeName = name
      this.reloadKey++
    },
    // 切应用/登出:标签清空。新应用首页由 router.afterEach 的 addTab 自然补成第一个(affix)标签。
    clearTabs() {
      this.tabs = []
    },
  },
  persist: { storage: sessionStorage, pick: ['tabs'] },
})
