import { useAuthStore } from '@/stores/auth'
import { useUserStore } from '@/stores/user'
import { personalApi } from '@/api'

export type EnterResult = { chooser: true } | { chooser: false; moduleId: number }

/**
 * 门户:登录后决定直接进 / 进默认 / 弹选择器,以及切换应用。对应 Vue 侧 `composables/useModule.ts`。
 * 与 UI 库无关,写成模块级纯 async 函数(不是 hook)——守卫、选择页都要调,不该绑组件生命周期。
 *
 * **与 Vue 的关键差异**:Vue 版 `enter` 命令式 `addRoute` 把动态路由挂上去;这里只把 `menuTree`
 * 写进 store,路由由 `buildRoutes(menuTree)` 经 `useRoutes` **反应式派生**。所以没有"挂了但当前 URL
 * 没重新匹配"的空窗,也就不需要 Vue 版 `router/index.ts` 那个 `return to.fullPath` 重解析 trick。
 */

/** 拉某应用的菜单树 → 写进 auth store(路由随之派生)。 */
export async function enter(moduleId: number): Promise<EnterResult> {
  const tree = await personalApi.menu(moduleId)
  useAuthStore.setState({ menuTree: tree, currentModuleId: moduleId, routesReady: true })
  return { chooser: false, moduleId }
}

/**
 * 登录进门户 / F5 深链重建:并行拉模块 + 权限码 + 资料,按阶梯决定落点。
 *
 * 权限码喂 `hasPerm`:成功(哪怕空集=超管)才标 `permissionsLoaded`;失败不阻断进门户,
 * 但 `permissionsLoaded` 保持 false → `hasPerm` fail-closed(藏按钮),不谎报"有权限"。
 * profile 拿超管标记(只对超管 fail-open)+ 顺手回填头像;**失败按普通用户处理(安全侧,不误放行)**。
 */
export async function enterInitial(): Promise<EnterResult> {
  const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
    personalApi.modules(),
    personalApi
      .permissions()
      .then((codes) => ({ ok: true, codes }))
      .catch(() => ({ ok: false, codes: [] as string[] })),
    personalApi
      .profile()
      .then((p) => ({ sadm: p.isSuperAdmin, avatar: p.avatar ?? null }))
      .catch(() => ({ sadm: false, avatar: null })),
  ])

  useAuthStore.setState({
    modules,
    defaultModuleId: defaultModuleId ?? null,
    permissionCodes: perm.codes,
    permissionsLoaded: perm.ok,
    isSuperAdmin: profile.sadm,
  })
  // 顶栏头像:登录出参不含 avatar,这里回填(取不到按无头像,顶栏回落图标)。
  useUserStore.setState((s) => (s.userInfo ? { userInfo: { ...s.userInfo, avatar: profile.avatar } } : {}))

  if (modules.length === 0) return { chooser: true } // 空态:选择器提示未分配应用
  // F5/深链优先重建"上次所在应用"(持久化的 currentModuleId),让其动态路由复活,跨应用深链不落 404。
  // 但必须**校验它仍在列表里**——应用被收回权限后 remembered 会指向一个不存在的应用。
  const remembered = useAuthStore.getState().currentModuleId
  if (remembered && modules.some((m) => m.id === remembered)) return enter(remembered)
  if (modules.length === 1) return enter(modules[0]!.id)
  if (defaultModuleId && modules.some((m) => m.id === defaultModuleId)) return enter(defaultModuleId)
  return { chooser: true }
}
