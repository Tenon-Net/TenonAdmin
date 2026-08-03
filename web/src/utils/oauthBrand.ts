// 外部登录品牌 UI 纯逻辑(D1-①):可被 vitest 直接驱动,不依赖浏览器。
// 契约见 docs/external-login-brand/decisions.md(I-A / O-A / B-A / N=4)。

/** 登录页最多平铺的品牌圆钮数;超出进「…」菜单。 */
export const SSO_VISIBLE_MAX = 4

/** 预置精修品牌标的 provider code(大小写不敏感)。 */
export const BRAND_CODES = ['github', 'wechat', 'wecom', 'dingtalk', 'gitee', 'qq'] as const
export type BrandCode = (typeof BRAND_CODES)[number]

const BRAND_SET = new Set<string>(BRAND_CODES)

/**
 * 登录页临时展示全部品牌圆标(图标验收用)。
 * true:铺全 6 个,不做 N=4 溢出;false:恢复仅后端 enabled providers。
 * 验收完请改回 false。
 */
export const PREVIEW_ALL_SSO_BRANDS = false

export const BRAND_DISPLAY_NAMES: Record<BrandCode, string> = {
  github: 'GitHub',
  wechat: '微信',
  wecom: '企业微信',
  dingtalk: '钉钉',
  gitee: 'Gitee',
  qq: 'QQ',
}

/** 图标预览用的假 provider 列表(与后端是否启用无关)。 */
export function previewAllBrandProviders(): SsoProviderLike[] {
  return BRAND_CODES.map((code) => ({ code, displayName: BRAND_DISPLAY_NAMES[code] }))
}

/** 配置页一行:内置品牌全展示;registered=后端已装包/已配密钥。 */
export type ConfigProviderRow = {
  code: string
  displayName: string
  icon?: string | null
  registered: boolean
  enabled: boolean
}

/**
 * 配置页列表 = 预置品牌(全量) + 其它已注册 code(如 oidc-demo)。
 * 未注册项 registered=false、enabled 强制 false(不可开登录页显示)。
 */
export function buildConfigProviderRows(
  registered: readonly { code: string; displayName: string; icon?: string | null; enabled: boolean }[],
): ConfigProviderRow[] {
  const byCode = new Map(registered.map((p) => [p.code, p]))
  const rows: ConfigProviderRow[] = []
  const seen = new Set<string>()

  for (const code of BRAND_CODES) {
    seen.add(code)
    const r = byCode.get(code)
    rows.push({
      code,
      displayName: r?.displayName || BRAND_DISPLAY_NAMES[code],
      icon: r?.icon ?? null,
      registered: !!r,
      enabled: r ? r.enabled : false,
    })
  }
  for (const p of registered) {
    if (seen.has(p.code)) continue
    seen.add(p.code)
    rows.push({
      code: p.code,
      displayName: p.displayName,
      icon: p.icon,
      registered: true,
      enabled: p.enabled,
    })
  }
  return rows
}

export type SsoProviderLike = {
  code: string
  displayName: string
  icon?: string | null
}

export type BindingLike = {
  provider: string
  displayName?: string | null
  boundAt: string
}

export type IconResolve =
  | { kind: 'brand'; code: BrandCode }
  | { kind: 'iconify'; name: string }
  | { kind: 'letter'; letter: string }

export type BindingRow = {
  code: string
  displayName: string
  icon?: string | null
  /** false = 运营已关但仍有绑定(B-A) */
  enabled: boolean
  binding?: BindingLike
}

/** 是否为可渲染的 Iconify 名称;拒绝 URL / data URI。 */
export function isIconifyName(icon: string | null | undefined): boolean {
  if (icon == null) return false
  const s = icon.trim()
  if (!s) return false
  if (/^https?:\/\//i.test(s) || s.startsWith('//') || /^data:/i.test(s)) return false
  // collection:name(离线 Iconify 常见形态)
  return /^[a-z0-9][a-z0-9-]*:[a-z0-9][a-z0-9._-]*$/i.test(s)
}

export function isBrandCode(code: string | null | undefined): code is BrandCode {
  return !!code && BRAND_SET.has(code.toLowerCase())
}

/** brand map → Iconify 名 → 首字母(I-A)。 */
export function resolveProviderIcon(code: string, icon?: string | null): IconResolve {
  const c = (code ?? '').trim().toLowerCase()
  if (isBrandCode(c)) return { kind: 'brand', code: c }
  if (isIconifyName(icon)) return { kind: 'iconify', name: icon!.trim() }
  const letter = (code ?? '?').trim().charAt(0).toUpperCase() || '?'
  return { kind: 'letter', letter }
}

/** 严格保序切分:前 max 平铺,其余进溢出(O-A)。 */
export function splitLoginProviders<T>(providers: readonly T[], maxVisible = SSO_VISIBLE_MAX): {
  visible: T[]
  overflow: T[]
} {
  const list = providers.slice()
  if (list.length <= maxVisible) return { visible: list, overflow: [] }
  return { visible: list.slice(0, maxVisible), overflow: list.slice(maxVisible) }
}

/**
 * 绑定页行:已启用 providers(API 序) ∪ 仅存在于 bindings 的已停用项(接在后面)(B-A)。
 * 不做 N=4 截断。
 */
export function mergeBindingRows(providers: readonly SsoProviderLike[], bindings: readonly BindingLike[]): BindingRow[] {
  const bindByProvider = new Map<string, BindingLike>()
  for (const b of bindings) bindByProvider.set(b.provider, b)

  const rows: BindingRow[] = []
  const seen = new Set<string>()

  for (const p of providers) {
    seen.add(p.code)
    rows.push({
      code: p.code,
      displayName: p.displayName,
      icon: p.icon,
      enabled: true,
      binding: bindByProvider.get(p.code),
    })
  }

  for (const b of bindings) {
    if (seen.has(b.provider)) continue
    rows.push({
      code: b.provider,
      displayName: (b.displayName && b.displayName.trim()) || b.provider,
      icon: null,
      enabled: false,
      binding: b,
    })
  }

  return rows
}
