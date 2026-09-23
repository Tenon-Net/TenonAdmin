import { useAuthStore } from '@/stores/auth'
import { useTabsStore } from '@/stores/tabs'
import { useUserStore } from '@/stores/user'
import { personalApi } from '@/api'
import { buildRoutesForModule } from './useAuthMenu'
import { router } from '@/router'

type EnterResult = { chooser: true } | { chooser: false; moduleId: number }

export function useModule() {
  const auth = useAuthStore()

  async function enter(moduleId: number): Promise<EnterResult> {
    await buildRoutesForModule(moduleId)
    return { chooser: false, moduleId }
  }

  async function enterInitial(): Promise<EnterResult> {
    // profile 失败沿用登录快照；无快照时按普通用户处理。
    const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
      personalApi.modules(),
      personalApi.permissions().then((codes) => ({ ok: true, codes })).catch(() => ({ ok: false, codes: [] as string[] })),
      personalApi.profile().then((p) => ({ sadm: p.isSuperAdmin, avatar: p.avatar ?? null })).catch(() => ({ sadm: useUserStore().userInfo?.isSuperAdmin ?? false, avatar: null })),
    ])
    auth.modules = modules
    auth.defaultModuleId = defaultModuleId ?? null
    auth.permissionCodes = perm.codes
    auth.permissionsLoaded = perm.ok
    auth.isSuperAdmin = profile.sadm
    const user = useUserStore()
    if (user.userInfo) user.userInfo.avatar = profile.avatar
    if (modules.length === 0) return { chooser: true }
    // 只恢复当前仍有权访问的上次应用。
    const remembered = auth.currentModuleId
    if (remembered && modules.some((m) => m.id === remembered)) return enter(remembered)
    if (modules.length === 1) return enter(modules[0]!.id)
    if (defaultModuleId && modules.some((m) => m.id === defaultModuleId)) return enter(defaultModuleId)
    return { chooser: true }
  }

  async function switchModule(moduleId: number): Promise<void> {
    await enter(moduleId)
    useTabsStore().clearTabs()
    router.replace(auth.homePath)
  }

  async function setDefault(moduleId: number): Promise<void> {
    await personalApi.setDefaultModule(moduleId)
    auth.defaultModuleId = moduleId
  }

  return { enter, enterInitial, switchModule, setDefault }
}
