import { useCallback } from 'react'
import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { AppModule, MenuNode } from '@/types/menu'

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
  /** 用户的默认应用(后端 SysUser.DefaultModuleId),随 enterInitial 拉取;不持久化,仅供选择页标记。 */
  defaultModuleId: number | null
  menuTree: MenuNode[]
  /** 登录进门户时由 useModule.enterInitial 填入(GET /personal/permissions);超管为空集 → fail-open,服务端 sadm 兜底。 */
  permissionCodes: string[]
  /**
   * 权限码是否已成功拉取。区分"取码失败/未加载"(fail-closed 藏按钮)与"已加载"(按码匹配)。
   * 不持久化——F5 后由守卫重新拉取。
   */
  permissionsLoaded: boolean
  /**
   * 当前用户是否超管;由 enterInitial 经 /personal/profile 填入,不持久化。
   * 只对超管 fail-open;普通用户空集则按码匹配(必然不命中 → 隐藏),避免"空集=超管"的误放行。
   */
  isSuperAdmin: boolean
  routesReady: boolean
  reset: () => void
}

/**
 * 当前应用的首页:模块自己的 DefaultRoute → 菜单树第一个叶子 → 应用选择器兜底。
 * 每个应用一个首页,所以 '/' 的落点不能写死。
 * 兜底是 /module 而非某个固定页:一个菜单都没配的应用没有首页可言,把人送回选择器,
 * 好过让他撞上一个"不属于本应用"的路径吃 404。
 */
export function homePath(s: Pick<AuthState, 'modules' | 'currentModuleId' | 'menuTree'>): string {
  const m = s.modules.find((x) => x.id === s.currentModuleId)
  return m?.defaultRoute || firstLeafPath(s.menuTree) || '/module'
}

/**
 * 按钮级权限判定。超管 → 全放行;取码失败/未加载 → fail-closed(不谎报"有");
 * 否则精确命中权限码(= 规范化路由 `{METHOD}:/{route}`)。
 *
 * 写成**纯函数**而不是 store 里返回闭包的选择器:zustand 的选择器每次渲染都会被调用并与上次结果
 * `Object.is` 比对,返回新建函数 = 每次都"变了" = 无限重渲染。判定规则收敛在这里,
 * 反应式取用走下面的 `useHasPerm()`,非组件处直接 `hasPerm(useAuthStore.getState(), code)`。
 */
export function hasPerm(
  s: Pick<AuthState, 'isSuperAdmin' | 'permissionsLoaded' | 'permissionCodes'>,
  code: string,
): boolean {
  return s.isSuperAdmin ? true : s.permissionsLoaded && s.permissionCodes.includes(code)
}

/**
 * 净态。`reset()` 与初始化共用一份,免得加字段时漏改其中一处(Vue 侧 reset 是逐字段手写,正是那个风险)。
 * 写成**工厂**而不是常量:常量的话三个数组在初始态与每次 reset 之间是**同一个实例**,
 * 谁写一句 `permissionCodes.sort()` 就永久污染了净态,此后每次 reset 都带脏数据。
 */
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

/**
 * 授权态:模块 / 菜单树 / 权限码 / 路由是否已建。对应 Vue 侧 `stores/auth.ts`。
 * **只持久化 currentModuleId 一项**——F5/深链时守卫据它重建"上次所在应用"的动态路由,
 * 跨应用深链(默认应用 ≠ 当前应用)才不会落 404。
 * menuTree/modules/routesReady 一律不持久化:动态路由只活在内存里,刷新后由守卫重建;
 * 尤其 routesReady 若被持久化成 true,F5 会跳过重建导致每条动态路由 404。
 */
export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      ...EMPTY(),
      // Vue 侧此处还连带 `useTabsStore().clearTabs()`。tabs 整体推迟到 D1(见 docs/react-template-ledger.md B3 记录),
      // 那条 store 落地时**必须**把清标签补回这里 —— 以及 `useModule` 切应用那一处(Vue 侧 useModule.ts:47),
      // 两处都要,否则登出换号 / 切应用会看见上一批标签。
      reset: () => set(EMPTY()),
    }),
    { name: 'auth', partialize: (s) => ({ currentModuleId: s.currentModuleId }) },
  ),
)

/** 反应式首页路径。 */
export function useHomePath(): string {
  return useAuthStore(homePath)
}

/**
 * 反应式权限判定。分三条细选择器订阅而不是整体订阅 store:后者任何一个无关字段
 * (menuTree、routesReady)变动都会让所有带权限门的按钮重渲染。
 */
export function useHasPerm(): (code: string) => boolean {
  const isSuperAdmin = useAuthStore((s) => s.isSuperAdmin)
  const permissionsLoaded = useAuthStore((s) => s.permissionsLoaded)
  const permissionCodes = useAuthStore((s) => s.permissionCodes)
  return useCallback(
    (code: string) => hasPerm({ isSuperAdmin, permissionsLoaded, permissionCodes }, code),
    [isSuperAdmin, permissionsLoaded, permissionCodes],
  )
}
