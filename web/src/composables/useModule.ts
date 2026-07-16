import { useAuthStore } from '@/stores/auth'
import { useTabsStore } from '@/stores/tabs'
import { personalApi } from '@/api'
import { buildRoutesForModule } from './useAuthMenu'
import { router } from '@/router'

type EnterResult = { chooser: true } | { chooser: false; moduleId: number }

/** 门户:登录后决定直接进 / 进默认 / 弹选择器,以及切换应用。逻辑与 Naive 无关。 */
export function useModule() {
  const auth = useAuthStore()

  async function enter(moduleId: number): Promise<EnterResult> {
    await buildRoutesForModule(moduleId)
    return { chooser: false, moduleId }
  }

  async function enterInitial(): Promise<EnterResult> {
    // 并行拉模块 + 当前用户权限码。权限码喂 v-auth:成功(哪怕空集=超管)才标 loaded;
    // 失败不阻断进门户,但 permissionsLoaded 保持 false → v-auth fail-closed(藏按钮),不谎报"有权限"。
    // profile 只为拿超管标记喂 v-auth(只对超管 fail-open,普通用户空集则隐藏);失败按普通用户处理(安全侧,不误放行)。
    const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
      personalApi.modules(),
      personalApi.permissions().then((codes) => ({ ok: true, codes })).catch(() => ({ ok: false, codes: [] as string[] })),
      personalApi.profile().then((p) => ({ sadm: p.isSuperAdmin })).catch(() => ({ sadm: false })),
    ])
    auth.modules = modules
    auth.defaultModuleId = defaultModuleId ?? null
    auth.permissionCodes = perm.codes
    auth.permissionsLoaded = perm.ok
    auth.isSuperAdmin = profile.sadm
    if (modules.length === 0) return { chooser: true } // 空态:选择器里提示未分配应用
    // F5/深链优先重建"上次所在应用"(持久化的 currentModuleId),让其动态路由复活,跨应用深链不落 404。
    const remembered = auth.currentModuleId
    if (remembered && modules.some((m) => m.id === remembered)) return enter(remembered)
    if (modules.length === 1) return enter(modules[0]!.id)
    if (defaultModuleId && modules.some((m) => m.id === defaultModuleId)) return enter(defaultModuleId)
    return { chooser: true }
  }

  async function switchModule(moduleId: number): Promise<void> {
    await enter(moduleId)
    useTabsStore().clearTabs() // 切应用 → 标签归零(新应用路由已重建)
    router.replace(auth.homePath) // 落到新应用自己的首页
  }

  async function setDefault(moduleId: number): Promise<void> {
    await personalApi.setDefaultModule(moduleId)
    auth.defaultModuleId = moduleId // 本地同步,选择页角标立刻转移,不必重拉 /personal/modules
  }

  return { enter, enterInitial, switchModule, setDefault }
}
