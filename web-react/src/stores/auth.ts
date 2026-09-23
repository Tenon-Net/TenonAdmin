import { useCallback } from 'react'
import { create } from 'zustand'
import { persist } from 'zustand/middleware'
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

export interface AuthState {
  modules: AppModule[]
  currentModuleId: number | null
  /** 后端配置的默认应用，仅用于门户选路和选择页标记。 */
  defaultModuleId: number | null
  menuTree: MenuNode[]
  /** 空集不代表超管；超管身份由 isSuperAdmin 单独判定。 */
  permissionCodes: string[]
  /** 普通用户未成功加载权限时隐藏受控按钮；刷新后由守卫重新拉取。 */
  permissionsLoaded: boolean
  /** 超级管理员的按钮权限不受权限码集合限制。 */
  isSuperAdmin: boolean
  routesReady: boolean
  reset: () => void
}

// 无可用首页时回到应用选择器，避免跳进其他应用的固定路径。
export function homePath(s: Pick<AuthState, 'modules' | 'currentModuleId' | 'menuTree'>): string {
  const m = s.modules.find((x) => x.id === s.currentModuleId)
  return m?.defaultRoute || firstLeafPath(s.menuTree) || '/module'
}

// 纯函数避免 zustand 选择器每次返回新闭包；超管放行，未加载时拒绝，否则精确匹配。
export function hasPerm(
  s: Pick<AuthState, 'isSuperAdmin' | 'permissionsLoaded' | 'permissionCodes'>,
  code: string,
): boolean {
  return s.isSuperAdmin ? true : s.permissionsLoaded && s.permissionCodes.includes(code)
}

// 工厂确保每次 reset 都获得新的数组实例。
const EMPTY = (): Omit<AuthState, 'reset'> => ({
  modules: [],
  currentModuleId: null,
  defaultModuleId: null,
  menuTree: [],
  permissionCodes: [],
  permissionsLoaded: false,
  isSuperAdmin: false,
  routesReady: false,
})

// 只持久化 currentModuleId；动态路由刷新后必须由守卫重建，routesReady 不能跨刷新保留。
export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      ...EMPTY(),
      reset: () => {
        set(EMPTY())
        useTabsStore.getState().clearTabs()
      },
    }),
    { name: 'auth', partialize: (s) => ({ currentModuleId: s.currentModuleId }) },
  ),
)

export function useHomePath(): string {
  return useAuthStore(homePath)
}

// 分字段订阅，避免无关授权状态变化触发权限门重渲染。
export function useHasPerm(): (code: string) => boolean {
  const isSuperAdmin = useAuthStore((s) => s.isSuperAdmin)
  const permissionsLoaded = useAuthStore((s) => s.permissionsLoaded)
  const permissionCodes = useAuthStore((s) => s.permissionCodes)
  return useCallback(
    (code: string) => hasPerm({ isSuperAdmin, permissionsLoaded, permissionCodes }, code),
    [isSuperAdmin, permissionsLoaded, permissionCodes],
  )
}
