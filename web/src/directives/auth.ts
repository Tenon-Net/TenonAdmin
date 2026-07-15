import type { Directive } from 'vue'
import { useAuthStore } from '@/stores/auth'

/**
 * v-auth 按钮级权限:权限码不命中则移除 DOM 节点。
 *   v-auth="'POST:/api/v1/sys/user'"     单码
 *   v-auth="['a','b']"                   多码,默认 OR
 *   v-auth.and="['a','b']"               多码,AND
 *
 * 权限码由 useModule.enterInitial 经 GET /personal/permissions 填入 authStore,超管标记经 /personal/profile 填入。
 * 显隐规则(服务端始终是权威,403/41001 兜底;这里只管 UED 不谎报):
 *   - **超管**(isSuperAdmin)→ **fail-open** 显示全部,服务端 sadm 绕过兜底。
 *   - 取码**失败/未加载**(permissionsLoaded=false)→ **fail-closed** 藏按钮:不知道有没有权限,谎报"有"比谎报"无"糟。
 *   - 已加载的普通用户 → 按码匹配;空集必然不命中 → 隐藏(修正过去"空集=超管"把无权用户也放行的 bug)。
 * // ponytail: 不为完美 UX 加客户端强隐藏,服务端才是权威;这里只把显隐拨正。
 */
export const vAuth: Directive<HTMLElement, string | string[]> = {
  mounted(el, binding) {
    const auth = useAuthStore()
    if (auth.isSuperAdmin) return // 超管 → 显示全部
    if (!auth.permissionsLoaded) return void el.remove() // 取码失败/未加载 → fail-closed
    const codes = auth.permissionCodes
    const need = binding.value
    const mode = binding.modifiers.and ? 'every' : 'some'
    const ok = Array.isArray(need)
      ? need[mode]((c) => codes.includes(c))
      : codes.includes(need)
    if (!ok) el.remove()
  },
}
