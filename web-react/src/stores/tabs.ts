import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'
import { useAuthStore, homePath } from '@/stores/auth'

export interface TabItem {
  path: string // 主键 = KeepAlive 缓存键(React 路由无 name,用 path 当身份)
  fullPath: string // 最后一次访问的完整 URL(含 query),点标签导航到此
  title: string // 原始标题(可能是 i18n key)
  icon?: string
  affix?: boolean // 固定标签(= 当前应用首页),不可关闭
  pinned?: boolean // 用户手动固定(右键「固定」);同样不可关闭,但可取消
  titleFixed?: boolean // setTitle 置真:动态标题(如记录名),addTab 复访不再用原 title 覆盖
  noCache?: boolean // 排除出 KeepAlive(详情等瞬时页,复访本就该拉新数据)
}

/** addTab 入参:由有路由/菜单上下文的调用方(tab-sync)喂进来,store 保持 router-free。 */
export interface AddTabInput {
  path: string
  fullPath: string
  title: string
  icon?: string
  noCache?: boolean
}

interface TabsState {
  tabs: TabItem[]
  reloadKey: number // refreshTab 递增 → KeepAliveOutlet 逐出 excludeKey 的实例并重挂
  excludeKey: string // 临时逐出缓存的 path(refreshTab)
  addTab: (r: AddTabInput) => void
  /**
   * 关闭标签。affix/pinned 不可关(返 null)。关的是当前页时返回应导航到的邻居 fullPath,否则 null。
   * **导航留给调用方**(有 useNavigate 的组件)——对齐 useModule.switchModule 的 router-free 约定。
   */
  removeTab: (path: string, currentPath: string) => string | null
  closeOthers: (path: string, currentPath: string) => string | null
  closeAll: (currentPath: string) => string | null
  closeLeft: (path: string, currentPath: string) => string | null
  closeRight: (path: string, currentPath: string) => string | null
  togglePin: (path: string) => void
  setTitle: (path: string, title: string) => void
  refreshTab: (path: string) => void
  clearExclude: () => void
  clearTabs: () => void
}

/** 当前应用首页(affix 判定基准);读 auth store 纯态,不触发导航。 */
const isAffix = (path: string) => path === homePath(useAuthStore.getState())

export const useTabsStore = create<TabsState>()(
  persist(
    (set, get) => ({
      tabs: [],
      reloadKey: 0,
      excludeKey: '',

      addTab: (r) =>
        set((s) => {
          const existing = s.tabs.find((t) => t.path === r.path)
          if (existing) {
            // 复访:更新 fullPath;动态标题(titleFixed)不被原 title 覆盖回去。
            return {
              tabs: s.tabs.map((t) =>
                t.path !== r.path ? t : { ...t, fullPath: r.fullPath, title: t.titleFixed ? t.title : r.title, icon: r.icon ?? t.icon },
              ),
            }
          }
          return {
            tabs: [...s.tabs, { path: r.path, fullPath: r.fullPath, title: r.title, icon: r.icon, affix: isAffix(r.path), noCache: r.noCache === true }],
          }
        }),

      removeTab: (path, currentPath) => {
        const s = get()
        const idx = s.tabs.findIndex((t) => t.path === path)
        if (idx === -1 || s.tabs[idx]!.affix || s.tabs[idx]!.pinned) return null
        const next = s.tabs.filter((_, i) => i !== idx)
        set({ tabs: next })
        if (currentPath !== path) return null // 关的不是当前页 → 不导航
        // 移到"补进被关位置"的右邻,退而求左邻,再退到首个。
        const nbr = next[idx] ?? next[idx - 1] ?? next[0]
        return nbr ? nbr.fullPath : null
      },

      closeOthers: (path, currentPath) => {
        set((s) => ({ tabs: s.tabs.filter((t) => t.affix || t.pinned || t.path === path) }))
        return ensureActive(get, currentPath, path)
      },
      closeAll: (currentPath) => {
        set((s) => ({ tabs: s.tabs.filter((t) => t.affix || t.pinned) }))
        return ensureActive(get, currentPath, homePath(useAuthStore.getState()))
      },
      closeLeft: (path, currentPath) => {
        const idx = get().tabs.findIndex((t) => t.path === path)
        if (idx <= 0) return null
        set((s) => ({ tabs: s.tabs.filter((t, i) => t.affix || t.pinned || i >= idx) }))
        return ensureActive(get, currentPath, path)
      },
      closeRight: (path, currentPath) => {
        const idx = get().tabs.findIndex((t) => t.path === path)
        if (idx === -1) return null
        set((s) => ({ tabs: s.tabs.filter((t, i) => t.affix || t.pinned || i <= idx) }))
        return ensureActive(get, currentPath, path)
      },

      // 用户手动固定/取消(右键);应用首页 affix 恒固定,不受影响。
      togglePin: (path) => set((s) => ({ tabs: s.tabs.map((t) => (t.path === path && !t.affix ? { ...t, pinned: !t.pinned } : t)) })),

      // 详情页数据加载后设动态标题(如记录名);titleFixed 防 addTab 复访覆盖回去。
      setTitle: (path, title) => set((s) => ({ tabs: s.tabs.map((t) => (t.path === path ? { ...t, title, titleFixed: true } : t)) })),

      // 逐出缓存实例并发信号:KeepAliveOutlet 监听 reloadKey,删掉 excludeKey 的缓存条目令其重挂,再 clearExclude。
      refreshTab: (path) => set((s) => ({ excludeKey: path, reloadKey: s.reloadKey + 1 })),
      clearExclude: () => set({ excludeKey: '' }),

      // 切应用/登出:标签清空。新应用首页由 tab-sync 的 addTab 自然补成第一个(affix)标签。
      clearTabs: () => set({ tabs: [] }),
    }),
    { name: 'tabs', storage: createJSONStorage(() => sessionStorage), partialize: (s) => ({ tabs: s.tabs }) },
  ),
)

/**
 * KeepAlive 应常驻的 path 集(排除 noCache 瞬时页)。写成纯函数导出(仿 auth.ts 的 homePath/hasPerm):
 * KeepAliveOutlet 据此保留/逐出缓存,单测也能直接钉住"哪些页进缓存"这条契约。
 */
export function aliveKeys(tabs: TabItem[]): string[] {
  return tabs.filter((t) => !t.noCache).map((t) => t.path)
}

/** 当前激活标签被批量关闭后,导航到 preferPath(通常右键那个)或末尾标签;仍在则不动(返 null)。 */
function ensureActive(get: () => TabsState, currentPath: string, preferPath: string): string | null {
  const tabs = get().tabs
  if (tabs.some((t) => t.path === currentPath)) return null
  const target = tabs.find((t) => t.path === preferPath) ?? tabs[tabs.length - 1]
  return target ? target.fullPath : null
}
