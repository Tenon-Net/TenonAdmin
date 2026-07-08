import type { Directive } from 'vue'
import { useAuthStore } from '@/stores/auth'

/**
 * v-auth 按钮级权限:权限码不命中则移除 DOM 节点。
 *   v-auth="'POST:/api/v1/sys/user'"     单码
 *   v-auth="['a','b']"                   多码,默认 OR
 *   v-auth.and="['a','b']"               多码,AND
 *
 * ⚠ 后端暂无"返回按钮权限码"接口 → permissionCodes 恒空 → **fail-open**(不隐藏)。
 * 真正的强制在服务端(403 / code 41001)。等后端补 /personal/permissions 后此指令自动生效。
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
