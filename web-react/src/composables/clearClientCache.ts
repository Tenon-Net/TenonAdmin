import { personalApi } from '@/api'
import { enter } from '@/composables/useModule'
import { useAuthStore } from '@/stores/auth'
import { useDictStore } from '@/stores/dict'
import { useSiteStore } from '@/stores/site'

/**
 * 清除前端会话缓存并尽量从服务端重拉:
 * - 字典内存缓存全清
 * - 站点品牌配置强制重取
 * - 权限码 / 超管标记重取
 * - 当前应用菜单树重建(路由随 store 派生)
 *
 * 不碰登录态、外观偏好、标签页——只解决"改了字典/菜单/权限还看到旧数据"。
 * 对齐 Vue 侧 `composables/clearClientCache.ts`。
 */
export async function clearClientCache(): Promise<void> {
  useDictStore.getState().invalidate()
  await useSiteStore.getState().load(true)

  const [perm, profile] = await Promise.all([
    personalApi
      .permissions()
      .then((codes) => ({ ok: true as const, codes }))
      .catch(() => ({ ok: false as const, codes: [] as string[] })),
    personalApi
      .profile()
      .then((p) => ({ sadm: p.isSuperAdmin }))
      .catch(() => ({ sadm: false })),
  ])
  useAuthStore.setState({
    permissionCodes: perm.codes,
    permissionsLoaded: perm.ok,
    isSuperAdmin: profile.sadm,
  })

  const moduleId = useAuthStore.getState().currentModuleId
  if (moduleId != null) {
    await enter(moduleId)
  }
}
