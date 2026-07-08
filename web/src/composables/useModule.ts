import { useAuthStore } from '@/stores/auth'
import { personalApi } from '@/api'
import { buildRoutesForModule } from './useAuthMenu'
import { router } from '@/router'
import type { MenuNode } from '@/types/menu'

type EnterResult = { chooser: true } | { chooser: false; moduleId: number }

function firstLeafPath(tree: MenuNode[]): string | undefined {
  for (const n of tree) {
    if (n.path && !n.children?.length) return n.path
    const deeper = n.children?.length ? firstLeafPath(n.children) : undefined
    if (deeper) return deeper
  }
  return undefined
}

/** 门户:登录后决定直接进 / 进默认 / 弹选择器,以及切换应用。逻辑与 Naive 无关。 */
export function useModule() {
  const auth = useAuthStore()

  async function enter(moduleId: number): Promise<EnterResult> {
    await buildRoutesForModule(moduleId)
    return { chooser: false, moduleId }
  }

  async function enterInitial(): Promise<EnterResult> {
    const { modules, defaultModuleId } = await personalApi.modules()
    auth.modules = modules
    if (modules.length === 0) return { chooser: true } // 空态:选择器里提示未分配应用
    if (modules.length === 1) return enter(modules[0]!.id)
    if (defaultModuleId && modules.some((m) => m.id === defaultModuleId)) return enter(defaultModuleId)
    return { chooser: true }
  }

  async function switchModule(moduleId: number): Promise<void> {
    await enter(moduleId)
    const m = auth.modules.find((x) => x.id === moduleId)
    router.replace(m?.defaultRoute || firstLeafPath(auth.menuTree) || '/workbench')
  }

  async function setDefault(moduleId: number): Promise<void> {
    await personalApi.setDefaultModule(moduleId)
  }

  return { enter, enterInitial, switchModule, setDefault }
}
