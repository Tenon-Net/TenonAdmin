import type { ApiId } from '@/types/api'

/** 工作流 int64 标识：JSON number 与十进制 string 都可接受，string 不做数值化。 */
export type WfId = ApiId

/** 仅接受安全正整数或正十进制字符串；字符串原样返回，避免雪花 Id 精度丢失。 */
export function normalizeWfId(value: unknown): WfId | null {
  if (typeof value === 'number') {
    return Number.isSafeInteger(value) && value > 0 ? value : null
  }
  if (typeof value === 'string' && /^\d+$/.test(value) && /[1-9]/.test(value)) {
    return value
  }
  return null
}

/** 实例详情路由;id 精度已丢失(不安全 number 或非法值)时返回 null,调用方应拒绝导航(#56)。 */
export function wfInstanceDetailPath(value: unknown): string | null {
  const id = normalizeWfId(value)
  return id == null ? null : `/workflow/instance/${id}/detail`
}

export function isPositiveWfId(value: unknown): value is WfId {
  return normalizeWfId(value) !== null
}

/** 后端可能在不同响应中把同一个 int64 编码成 number 或 string。 */
export function wfIdEquals(left: WfId | null | undefined, right: WfId | null | undefined): boolean {
  return left != null && right != null && String(left) === String(right)
}

/** OpenAPI 路径接受 number|string，原样传递十进制字符串。 */
export function workflowPathId(id: WfId): WfId {
  return id
}
