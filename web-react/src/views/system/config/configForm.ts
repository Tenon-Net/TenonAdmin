// 分类配置中心纯逻辑(变异钉):字段常量 + 各分组 parse(后端行→表单态)/serialize(表单态→saveBatch 项)
// + 后缀规范化 + 其他配置页的表单映射。值序列化/反序列化是易错点(布尔 'true' 串、数值 min 兜底、后缀补点小写),
// 全抽到这里逐条钉;UI 接线在各 *.tsx。对齐 Vue 侧 config/components/*.vue 的同名逻辑。
import type { ConfigInput, SysConfig } from '@/types/api'

/** saveBatch 项:仅回写键与值(结构化表单保存)。 */
export interface BatchItem {
  configKey: string
  configValue?: string | null
}
type ConfigRow = Pick<SysConfig, 'configKey' | 'configValue'>

/** 行数组 → key→value 映射(value 空值归一空串);只含 rows 里出现的键。 */
export function rowsToMap(rows: ConfigRow[]): Map<string, string> {
  return new Map(rows.map((r) => [r.configKey, r.configValue ?? '']))
}

// ── 系统基础(GroupCode='sys')──
// 结构化字段:key 绑后端配置键,label 走 i18n。经匿名 siteInfo 下发,真实消费点在登录页/侧栏/顶栏/页脚。
export const SYS_FIELDS = [
  'sys.site.title',
  'sys.site.logo',
  'sys.site.subtitle',
  'sys.site.copyright',
  'sys.site.copyrightUrl',
] as const

export function parseBase(rows: ConfigRow[]): Record<string, string> {
  const map = rowsToMap(rows)
  const values: Record<string, string> = {}
  for (const k of SYS_FIELDS) values[k] = map.get(k) ?? ''
  return values
}
export function serializeBase(values: Record<string, string>): BatchItem[] {
  return SYS_FIELDS.map((k) => ({ configKey: k, configValue: values[k] ?? '' }))
}

// ── 上传策略(GroupCode='upload')──
export const KEY_MAX_SIZE = 'sys.upload.maxSizeMb'
export const KEY_ALLOWED = 'sys.upload.allowedExtensions'

/** 后缀规范化:补前导点 + 转小写(与后端 ParseExts 对齐),避免存 "JPG"/"jpg" 之类脏值。 */
export function normalizeExt(v: string): string {
  const s = v.trim().toLowerCase()
  return s.startsWith('.') ? s : '.' + s
}
export function parseUpload(rows: ConfigRow[]): { maxSizeMb: number; exts: string[] } {
  const map = rowsToMap(rows)
  return {
    maxSizeMb: Number(map.get(KEY_MAX_SIZE)) || 20, // 空/NaN/0 一律回退 20(与 Vue 一致)
    exts: (map.get(KEY_ALLOWED) ?? '').split(',').map((e) => e.trim()).filter(Boolean),
  }
}
export function serializeUpload(maxSizeMb: number, exts: string[]): BatchItem[] {
  return [
    { configKey: KEY_MAX_SIZE, configValue: String(maxSizeMb) },
    { configKey: KEY_ALLOWED, configValue: exts.map(normalizeExt).join(',') },
  ]
}

// ── 安全策略(GroupCode='security')──
// 数值项(min 兜底:锁死阈值不为负、时长至少 1 分钟,避免存 0 导致永不过期/永久锁定)。
export const NUM_FIELDS = [
  { key: 'sys.security.loginLock.maxFailCount', min: 0 },
  { key: 'sys.security.loginLock.lockMinutes', min: 1 },
  { key: 'sys.security.password.minLength', min: 1 },
  { key: 'sys.security.password.expireDays', min: 0 }, // 0 = 永不过期(合法值,不同于其余 min:1 项)
  { key: 'sys.security.session.accessMinutes', min: 1 },
  { key: 'sys.security.session.refreshMinutes', min: 1 },
  { key: 'sys.security.rateLimit.windowSeconds', min: 1 },
  { key: 'sys.security.rateLimit.permitPerWindow', min: 0 },
  { key: 'sys.security.rateLimit.authPermitPerWindow', min: 0 },
] as const

// 密码复杂度四开关(单独成节循环渲染)。
export const PWD_BOOL_FIELDS = [
  'sys.security.password.requireUpper',
  'sys.security.password.requireLower',
  'sys.security.password.requireDigit',
  'sys.security.password.requireSpecial',
] as const

export const CAPTCHA_KEY = 'sys.security.captcha.enabled'
export const CAPTCHA_TYPE_KEY = 'sys.security.captcha.type' // 字符串枚举:char/path/math
export const TOTP_KEY = 'sys.security.totp.enabled'
export const TOTP_SUPER_KEY = 'sys.security.totp.requireForSuperAdmin'
export const SMS_MFA_KEY = 'sys.security.mfa.enabled' // 二次验证
export const SMS_LOGIN_KEY = 'sys.security.smsLogin.enabled' // 免密登录
export const RATELIMIT_KEY = 'sys.security.rateLimit.enabled'

/** 全部布尔键(密码 4 + 验证码 + TOTP 2 + mfa + smsLogin + 限流);load/save 统一按 'true' 串处理。 */
export const ALL_BOOL_KEYS = [
  ...PWD_BOOL_FIELDS,
  CAPTCHA_KEY,
  TOTP_KEY,
  TOTP_SUPER_KEY,
  SMS_MFA_KEY,
  SMS_LOGIN_KEY,
  RATELIMIT_KEY,
] as const

export interface SecurityState {
  nums: Record<string, number>
  bools: Record<string, boolean>
  captchaType: string
}

export function parseSecurity(rows: ConfigRow[]): SecurityState {
  const map = rowsToMap(rows)
  const nums: Record<string, number> = {}
  for (const f of NUM_FIELDS) nums[f.key] = Number(map.get(f.key)) || f.min
  const bools: Record<string, boolean> = {}
  for (const k of ALL_BOOL_KEYS) bools[k] = map.get(k) === 'true'
  return { nums, bools, captchaType: map.get(CAPTCHA_TYPE_KEY) || 'char' }
}
export function serializeSecurity(s: SecurityState): BatchItem[] {
  // 配置值统一以字符串落库;数值/布尔序列化为字符串。键集与 parseSecurity 对称。
  return [
    ...NUM_FIELDS.map((f) => ({ configKey: f.key, configValue: String(s.nums[f.key]) })),
    ...ALL_BOOL_KEYS.map((k) => ({ configKey: k, configValue: String(s.bools[k]) })),
    { configKey: CAPTCHA_TYPE_KEY, configValue: s.captchaType },
  ]
}

// ── 其他配置(通用兜底 CRUD)──
// 内置分组有专属结构化表单;其他页仍允许消费方自定义任意分组,但不重复展示内置项。
export const STRUCTURED_GROUPS = ['sys', 'security', 'upload', 'externalauth', 'job']

export function blankConfig(): ConfigInput {
  return { configKey: '', configValue: '', name: '', groupCode: '', sort: 0, remark: '' }
}
export function configToInput(r: SysConfig): ConfigInput {
  return {
    configKey: r.configKey,
    configValue: r.configValue ?? '',
    name: r.name,
    groupCode: r.groupCode ?? '',
    sort: r.sort,
    remark: r.remark ?? '',
  }
}
