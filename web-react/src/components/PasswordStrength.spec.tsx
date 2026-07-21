import { describe, it, expect, afterEach, beforeEach, vi } from 'vitest'
import { render, screen, cleanup, waitFor } from '@testing-library/react'
import '@/locales'

vi.mock('@/api', () => ({ configApi: { passwordPolicy: vi.fn() } }))
import { configApi } from '@/api'

import {
  computeChecks, computeStrength, buildRules, DEFAULT_POLICY, type PasswordPolicy,
  PasswordStrength,
} from './PasswordStrength'

afterEach(cleanup)

// buildRules 的 t 桩:返回键(+参数),避开真 i18n,只验分支与参数透传。
const tk = (k: string, o?: Record<string, unknown>) => (o ? `${k}|${JSON.stringify(o)}` : k)

// ── computeChecks ──
describe('computeChecks', () => {
  it('各字符类命中', () => {
    const c = computeChecks('Abcd1234!xyz', DEFAULT_POLICY)
    expect(c).toEqual({ minLength: true, upper: true, lower: true, digit: true, special: true })
  })
  it('minLength 边界 len==min 视为达标(>= 而非 >)', () => {
    expect(computeChecks('abcdefgh', DEFAULT_POLICY).minLength).toBe(true) // 8==8
    expect(computeChecks('abcdefg', DEFAULT_POLICY).minLength).toBe(false) // 7<8
  })
})

// ── computeStrength ──
describe('computeStrength', () => {
  const chk = (p: string) => computeStrength(p, computeChecks(p, DEFAULT_POLICY))
  it('空 → 0', () => {
    expect(chk('')).toBe(0)
  })
  it('纯小写 8 位(种类≤1)→ 1', () => {
    expect(chk('aaaaaaaa')).toBe(1)
  })
  it('短但多样(未达长度)→ 1', () => {
    expect(chk('Ab1!')).toBe(1) // len4 < 8:!minLength 命中
  })
  it('达标但短(<12)→ 2', () => {
    expect(chk('Abc12345')).toBe(2) // len8,种类3,长度<12
  })
  it('恰两种类且长度达标 → 2', () => {
    expect(chk('abcABCabcABC')).toBe(2) // len12,大小写两类
  })
  it('长且多样 → 3', () => {
    expect(chk('Abcd1234!xyz')).toBe(3) // len12,四类
  })
})

// ── buildRules ──
describe('buildRules', () => {
  const allTrue: PasswordPolicy = { minLength: 8, requireUpper: true, requireLower: true, requireDigit: true, requireSpecial: true }
  const checks = computeChecks('Abcd1234!', allTrue)
  it('minLength 行恒显且带 n 参', () => {
    const rows = buildRules(DEFAULT_POLICY, computeChecks('x', DEFAULT_POLICY), tk)
    expect(rows[0]).toEqual({ key: 'minLength', ok: false, text: 'changePassword.rules.minLength|{"n":8}' })
  })
  it('策略不要求大写 → 无 upper 行', () => {
    const rows = buildRules({ ...allTrue, requireUpper: false }, checks, tk)
    expect(rows.some((r) => r.key === 'upper')).toBe(false)
  })
  it('默认策略(不强制特殊字符)→ special 走可选提示', () => {
    const rows = buildRules(DEFAULT_POLICY, computeChecks('x', DEFAULT_POLICY), tk)
    expect(rows.find((r) => r.key === 'special')!.text).toBe('changePassword.rules.specialOptional')
  })
  it('策略强制特殊字符 → special 走硬规则', () => {
    const rows = buildRules(allTrue, checks, tk)
    expect(rows.find((r) => r.key === 'special')!.text).toBe('changePassword.rules.special')
  })
})

// ── 组件接线 ──
describe('PasswordStrength 组件', () => {
  beforeEach(() => {
    vi.mocked(configApi.passwordPolicy).mockResolvedValue(DEFAULT_POLICY)
  })

  it('空密码渲染 null', () => {
    const { container } = render(<PasswordStrength value="" />)
    expect(container.firstChild).toBeNull()
  })

  it('有密码 → 显示强度标签 + 默认策略最小长度规则', () => {
    render(<PasswordStrength value="abc" />)
    expect(screen.getByText(/密码强度/)).toBeTruthy()
    expect(screen.getByText('至少 8 位')).toBeTruthy()
  })

  it('拉到的策略覆盖默认(fetch 接线)', async () => {
    vi.mocked(configApi.passwordPolicy).mockResolvedValue({ ...DEFAULT_POLICY, minLength: 10 })
    render(<PasswordStrength value="abc" />)
    await waitFor(() => expect(screen.getByText('至少 10 位')).toBeTruthy())
  })
})
