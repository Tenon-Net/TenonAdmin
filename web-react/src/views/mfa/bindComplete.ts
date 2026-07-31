/**
 * bindComplete 已在服务端完成绑定；前端只能在拿到非空恢复码后进入展示步。
 * 缺字段 / null / 空数组 / 仅空白串 一律视为响应不完整，避免空白成功屏。
 */
export function takeRecoveryCodes(out: { recoveryCodes?: string[] | null }): string[] | null {
  const codes = (out.recoveryCodes ?? [])
    .map((c) => (typeof c === 'string' ? c.trim() : ''))
    .filter((c) => c.length > 0)
  return codes.length > 0 ? codes : null
}
