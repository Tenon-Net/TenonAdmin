import { personalApi } from '@/api'
import { buildRoutesForModule } from '@/composables/useAuthMenu'
import { loadSite } from '@/composables/useSite'
import { useAuthStore } from '@/stores/auth'
import { useDictStore } from '@/stores/dict'

/**
 * 清除前端会话缓存并尽量从服务端重拉:
 * - 字典内存缓存全清
 * - 站点品牌配置强制重取
 * - 权限码 / 超管标记重取
 * - 当前应用菜单树与动态路由重建
 *
 * 不碰登录态、外观偏好、标签页——只解决"改了字典/菜单/权限还看到旧数据"。
 */
export async function clearClientCache(): Promise<void> {
  useDictStore().invalidate()
  await loadSite(true)

  const auth = useAuthStore()
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
  auth.permissionCodes = perm.codes
  auth.permissionsLoaded = perm.ok
  auth.isSuperAdmin = profile.sadm

  if (auth.currentModuleId != null) {
    await buildRoutesForModule(auth.currentModuleId)
  }
}
