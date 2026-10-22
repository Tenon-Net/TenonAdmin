import { useAuthStore } from '@/stores/auth'
import { useTabsStore } from '@/stores/tabs'
import { useUserStore } from '@/stores/user'
import { personalApi } from '@/api'
import { buildRoutesForModule } from './useAuthMenu'
import { router } from '@/router'
import type { ApiId } from '@/types/api'

type EnterResult = { chooser: true } | { chooser: false; moduleId: ApiId }

export function useModule() {
  const auth = useAuthStore()

  async function enter(moduleId: ApiId): Promise<EnterResult> {
    await buildRoutesForModule(moduleId)
    return { chooser: false, moduleId }
  }

  async function enterInitial(): Promise<EnterResult> {
    // 三份启动数据共同决定菜单与按钮权限；任一失败都不能伪装成“空权限初始化成功”。
    const [{ modules, defaultModuleId }, permissionCodes, profile] = await Promise.all([
      personalApi.modules(),
      personalApi.permissions(),
      personalApi.profile(),
    ])
    auth.modules = modules
    auth.defaultModuleId = defaultModuleId ?? null
    auth.permissionCodes = permissionCodes
    auth.permissionsLoaded = true
    auth.isSuperAdmin = profile.isSuperAdmin
    auth.initializationError = ''
    auth.initializationRedirect = ''
    const user = useUserStore()
    if (user.userInfo) user.userInfo.avatar = profile.avatar ?? null
    if (modules.length === 0) return { chooser: true }
    // 只恢复当前仍有权访问的上次应用。
    const remembered = auth.currentModuleId
    if (remembered && modules.some((m) => m.id === remembered)) return enter(remembered)
    if (modules.length === 1) return enter(modules[0]!.id)
    if (defaultModuleId && modules.some((m) => m.id === defaultModuleId)) return enter(defaultModuleId)
    return { chooser: true }
  }

  async function switchModule(moduleId: ApiId): Promise<void> {
    await enter(moduleId)
    useTabsStore().clearTabs()
    router.replace(auth.homePath)
  }

  async function setDefault(moduleId: ApiId): Promise<void> {
    await personalApi.setDefaultModule(moduleId)
    auth.defaultModuleId = moduleId
  }

  return { enter, enterInitial, switchModule, setDefault }
}
