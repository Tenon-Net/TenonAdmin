// 日志三页共用的**纯逻辑**(变异钉);页面 index.tsx 只做列/抽屉接线。

/** 操作人展示:优先姓名,姓名缺失(用户已删/未解析/匿名端点)回落原始 Id,再无则占位。 */
export function operatorText(r: { operatorName?: string | null; operatorId?: number | null }): string {
  return r.operatorName || (r.operatorId != null ? String(r.operatorId) : '—')
}

/** 登录日志姓名列:姓名 → 账号 → 占位(登录失败/账号不存在的行无姓名,回落账号仍有排查价值)。 */
export function loginNameText(r: { name?: string | null; account?: string | null }): string {
  return r.name || r.account || '—'
}

/** paramJson 美化:parse→stringify(缩进 2);非法 JSON 原样返回(空值占位由调用方判)。 */
export function prettyParam(json: string): string {
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    return json
  }
}

// 登录结果码 → i18n 键。裸渲染的 40004 对排查毫无用处(密码错/被锁/被停用三种处置动作完全不同:
// 改密/解锁/启用),翻成人话且文案直指已有 error.auth.* 键(与登录页弹的提示同源)。
// AuthService 实际只落 0 与 40001–40005;未命中的码回 null,由调用方回落成原始数字。
const LOGIN_RESULT_KEYS: Record<number, string> = {
  0: 'log.success',
  40001: 'error.auth.passwordWrong',
  40002: 'error.auth.captchaExpired',
  40003: 'error.auth.captchaWrong',
  40004: 'error.auth.accountLocked',
  40005: 'error.auth.accountDisabled',
}
/** 登录结果码 → i18n 键;未命中回 null(调用方回落成原始数字)。 */
export function loginResultKey(code: number): string | null {
  return LOGIN_RESULT_KEYS[code] ?? null
}
