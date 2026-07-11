import type { Directive } from 'vue'
import { useAuthStore } from '@/stores/auth'

/**
 * v-auth 按钮级权限:权限码不命中则移除 DOM 节点。
 *   v-auth="'POST:/api/v1/sys/user'"     单码
 *   v-auth="['a','b']"                   多码,默认 OR
 *   v-auth.and="['a','b']"               多码,AND
 *
 * 权限码由 useModule.enterInitial 经 GET /personal/permissions 填入 authStore。
 * ⚠ 空集时 **fail-open**(不隐藏):超管无角色故码为空,靠此分支看到全部按钮(服务端 sadm 绕过兜底);
 *   无权限的普通用户同样 fail-open(看得到、点了 403)——真正的强制在服务端(403 / code 41001)。
 * // ponytail: 不为完美 UX 加客户端强隐藏,服务端才是权威。
 */
export const vAuth: Directive<HTMLElement, string | string[]> = {
  mounted(el, binding) {
    const codes = useAuthStore().permissionCodes
    if (!codes.length) return // fail-open:无权限码数据时不做隐藏
    const need = binding.value
    const mode = binding.modifiers.and ? 'every' : 'some'
    const ok = Array.isArray(need)
      ? need[mode]((c) => codes.includes(c))
      : codes.includes(need)
    if (!ok) el.remove()
  },
}
