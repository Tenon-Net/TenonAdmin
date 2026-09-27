import { useAuthStore, homePath } from '@/stores/auth'
import { useUserStore } from '@/stores/user'
import { useTabsStore } from '@/stores/tabs'
import { personalApi } from '@/api'

export type EnterResult = { chooser: true } | { chooser: false; moduleId: number }

// 模块级函数供守卫和页面复用；React 路由从 menuTree 反应式派生。

export async function enter(moduleId: number): Promise<EnterResult> {
  const tree = await personalApi.menu(moduleId)
  useAuthStore.setState({ menuTree: tree, currentModuleId: moduleId, routesReady: true })
  return { chooser: false, moduleId }
}

// 返回首页，由持有 router 上下文的调用方导航。
export async function switchModule(moduleId: number): Promise<string> {
  await enter(moduleId)
  // 旧应用的标签在新应用中可能成为死链。
  useTabsStore.getState().clearTabs()
  return homePath(useAuthStore.getState())
}

export async function setDefault(moduleId: number): Promise<void> {
  await personalApi.setDefaultModule(moduleId)
  useAuthStore.setState({ defaultModuleId: moduleId })
}

// 合并 StrictMode 重挂载和并发调用；settle 后清空，不缓存结果。
let inflight: Promise<EnterResult> | null = null

// profile 失败沿用登录快照；无快照时按普通用户处理。
export function enterInitial(): Promise<EnterResult> {
  inflight ??= doEnterInitial().finally(() => {
    inflight = null
  })
  return inflight
}

async function doEnterInitial(): Promise<EnterResult> {
  const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
    personalApi.modules(),
    personalApi
      .permissions()
      .then((codes) => ({ ok: true, codes }))
      .catch(() => ({ ok: false, codes: [] as string[] })),
    personalApi
      .profile()
      .then((p) => ({ sadm: p.isSuperAdmin, avatar: p.avatar ?? null }))
      .catch(() => ({ sadm: useUserStore.getState().userInfo?.isSuperAdmin ?? false, avatar: null })),
  ])

  useAuthStore.setState({
    modules,
    defaultModuleId: defaultModuleId ?? null,
    permissionCodes: perm.codes,
    permissionsLoaded: perm.ok,
    isSuperAdmin: profile.sadm,
  })
  useUserStore.setState((s) => (s.userInfo ? { userInfo: { ...s.userInfo, avatar: profile.avatar } } : {}))

  if (modules.length === 0) return { chooser: true }
  // 只恢复当前仍有权访问的上次应用。
  const remembered = useAuthStore.getState().currentModuleId
  if (remembered && modules.some((m) => m.id === remembered)) return enter(remembered)
  if (modules.length === 1) return enter(modules[0]!.id)
  if (defaultModuleId && modules.some((m) => m.id === defaultModuleId)) return enter(defaultModuleId)
  return { chooser: true }
}
