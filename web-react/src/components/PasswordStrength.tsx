import { useEffect, useState, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { configApi } from '@/api'

// ── 纯逻辑(变异钉死;组件只接线)──────────────────────────────

export interface PasswordPolicy {
  minLength: number
  requireUpper: boolean
  requireLower: boolean
  requireDigit: boolean
  requireSpecial: boolean
}

/** 默认 = 后端默认策略,作 fallback(拉取失败仍可用;后端始终强制,弱口令以 passwordTooWeak 兜底)。 */
export const DEFAULT_POLICY: PasswordPolicy = {
  minLength: 8,
  requireUpper: true,
  requireLower: true,
  requireDigit: true,
  requireSpecial: false,
}

export interface PwChecks {
  minLength: boolean
  upper: boolean
  lower: boolean
  digit: boolean
  special: boolean
}

/** 逐条校验密码是否满足各规则(minLength 依当前策略,其余按字符类)。 */
export function computeChecks(p: string, policy: PasswordPolicy): PwChecks {
  return {
    minLength: p.length >= policy.minLength,
    upper: /[A-Z]/.test(p),
    lower: /[a-z]/.test(p),
    digit: /\d/.test(p),
    special: /[^A-Za-z0-9]/.test(p),
  }
}

/** 强度:空→0;长度达标 + 字符种类数 → 弱(1)/中(2)/强(3)。逻辑逐字对齐 Vue 版。 */
export function computeStrength(p: string, c: PwChecks): 0 | 1 | 2 | 3 {
  if (!p) return 0
  const variety = [c.upper, c.lower, c.digit, c.special].filter(Boolean).length
  if (!c.minLength || variety <= 1) return 1
  if (variety === 2 || p.length < 12) return 2
  return 3
}

export interface PwRule {
  key: string
  ok: boolean
  text: string
}

/** 规则清单按有效策略动态构建:恒显最小长度;大小写/数字仅策略要求时作硬规则;
 *  特殊字符——策略要求时作硬规则,否则作可选提示。 */
export function buildRules(policy: PasswordPolicy, c: PwChecks, t: (k: string, o?: Record<string, unknown>) => string): PwRule[] {
  const rows: PwRule[] = [{ key: 'minLength', ok: c.minLength, text: t('changePassword.rules.minLength', { n: policy.minLength }) }]
  if (policy.requireUpper) rows.push({ key: 'upper', ok: c.upper, text: t('changePassword.rules.upper') })
  if (policy.requireLower) rows.push({ key: 'lower', ok: c.lower, text: t('changePassword.rules.lower') })
  if (policy.requireDigit) rows.push({ key: 'digit', ok: c.digit, text: t('changePassword.rules.digit') })
  rows.push({
    key: 'special',
    ok: c.special,
    text: policy.requireSpecial ? t('changePassword.rules.special') : t('changePassword.rules.specialOptional'),
  })
  return rows
}

// 强度档位对应色(index 0 占位,对齐 strength 取值 1/2/3)。
const STRENGTH_COLORS = ['', '#e88080', '#e0a458', '#5aa86e']

// ── 组件 ───────────────────────────────────────────────────────

const rulesWrap: CSSProperties = {
  margin: '8px 0 0',
  padding: 0,
  listStyle: 'none',
  display: 'grid',
  gridTemplateColumns: '1fr 1fr',
  gap: '2px 12px',
}

/**
 * 密码强度提示条 + 规则清单。自包含:传入密码字符串即可,内部 effect 拉后端当前生效策略
 * (超管在「安全策略」可改),精确同步不漂移。改密页与建用户页共用。对应 Vue 侧 `PasswordStrength/index.vue`。
 * ponytail: i18n 沿用 changePassword.* 键(既有,零迁移)。
 */
export function PasswordStrength({ value }: { value: string }) {
  const { t } = useTranslation()
  const [policy, setPolicy] = useState<PasswordPolicy>(DEFAULT_POLICY)
  useEffect(() => {
    configApi
      .passwordPolicy()
      .then(setPolicy)
      .catch(() => {
        /* 保留默认;后端仍强制 */
      })
  }, [])

  if (!value) return null

  const checks = computeChecks(value, policy)
  const strength = computeStrength(value, checks)
  const color = STRENGTH_COLORS[strength]
  const label = ['', t('changePassword.strength.weak'), t('changePassword.strength.fair'), t('changePassword.strength.strong')][strength]
  const rules = buildRules(policy, checks, t)

  return (
    <div style={{ margin: '-8px 0 14px' }}>
      <div style={{ display: 'flex', gap: 6 }}>
        {[1, 2, 3].map((i) => (
          <span key={i} style={{ flex: 1, height: 4, borderRadius: 2, background: i <= strength ? color : 'var(--color-border)', transition: 'background var(--transition-fast)' }} />
        ))}
      </div>
      <div style={{ marginTop: 6, fontSize: 12, color }}>
        {t('changePassword.strength.label')}:{label}
      </div>
      <ul style={rulesWrap}>
        {rules.map((r) => (
          <li key={r.key} style={{ display: 'flex', alignItems: 'center', gap: 4, fontSize: 12, color: r.ok ? '#5aa86e' : 'var(--color-text-tertiary)' }}>
            <AppIcon icon={r.ok ? 'ph:check-circle-fill' : 'ph:circle'} size={14} />
            {r.text}
          </li>
        ))}
      </ul>
    </div>
  )
}
