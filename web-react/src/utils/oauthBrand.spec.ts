import { describe, expect, it } from 'vitest'
import {
  isIconifyName,
  mergeBindingRows,
  resolveProviderIcon,
  splitLoginProviders,
  SSO_VISIBLE_MAX,
} from './oauthBrand'

describe('splitLoginProviders', () => {
  const codes = (n: number) => Array.from({ length: n }, (_, i) => ({ code: `p${i}`, displayName: `P${i}` }))

  it('0 providers → empty', () => {
    const r = splitLoginProviders([])
    expect(r.visible).toEqual([])
    expect(r.overflow).toEqual([])
  })

  it('1 and 4 → all visible, no overflow; preserves order', () => {
    for (const n of [1, 4]) {
      const list = codes(n)
      const r = splitLoginProviders(list)
      expect(r.visible.map((x) => x.code)).toEqual(list.map((x) => x.code))
      expect(r.overflow).toEqual([])
    }
  })

  it('5+ → first N visible, rest overflow; no reorder', () => {
    const list = codes(6)
    const r = splitLoginProviders(list)
    expect(r.visible.map((x) => x.code)).toEqual(['p0', 'p1', 'p2', 'p3'])
    expect(r.overflow.map((x) => x.code)).toEqual(['p4', 'p5'])
    expect(SSO_VISIBLE_MAX).toBe(4)
  })
})

describe('resolveProviderIcon / isIconifyName (I-A)', () => {
  it('known brand codes win over icon field', () => {
    expect(resolveProviderIcon('GitHub', 'mdi:something')).toEqual({ kind: 'brand', code: 'github' })
    expect(resolveProviderIcon('wechat')).toEqual({ kind: 'brand', code: 'wechat' })
    expect(resolveProviderIcon('wecom')).toEqual({ kind: 'brand', code: 'wecom' })
  })

  it('accepts Iconify names only', () => {
    expect(isIconifyName('mdi:github')).toBe(true)
    expect(isIconifyName('  ph:link-simple  ')).toBe(true)
    expect(isIconifyName('https://cdn.example/x.svg')).toBe(false)
    expect(isIconifyName('//evil/x')).toBe(false)
    expect(isIconifyName('data:image/svg+xml;base64,xx')).toBe(false)
    expect(isIconifyName('')).toBe(false)
    expect(isIconifyName(null)).toBe(false)
  })

  it('unknown code + iconify → iconify; URL → letter fallback', () => {
    expect(resolveProviderIcon('keycloak', 'mdi:key')).toEqual({ kind: 'iconify', name: 'mdi:key' })
    expect(resolveProviderIcon('keycloak', 'https://x/y.png')).toEqual({ kind: 'letter', letter: 'K' })
    expect(resolveProviderIcon('oidc', '')).toEqual({ kind: 'letter', letter: 'O' })
  })
})

describe('mergeBindingRows (B-A)', () => {
  it('lists all enabled providers without N=4 truncate', () => {
    const providers = Array.from({ length: 6 }, (_, i) => ({
      code: `p${i}`,
      displayName: `P${i}`,
    }))
    const rows = mergeBindingRows(providers, [])
    expect(rows).toHaveLength(6)
    expect(rows.every((r) => r.enabled)).toBe(true)
  })

  it('appends disabled-but-bound after enabled list', () => {
    const providers = [{ code: 'github', displayName: 'GitHub' }]
    const bindings = [
      { provider: 'github', displayName: 'octocat', boundAt: '2026-01-01T00:00:00Z' },
      { provider: 'wechat', displayName: null, boundAt: '2026-01-02T00:00:00Z' },
    ]
    const rows = mergeBindingRows(providers, bindings)
    expect(rows.map((r) => r.code)).toEqual(['github', 'wechat'])
    expect(rows[0].enabled).toBe(true)
    expect(rows[0].binding?.boundAt).toBe('2026-01-01T00:00:00Z')
    expect(rows[1].enabled).toBe(false)
    expect(rows[1].binding).toBeTruthy()
    expect(rows[1].displayName).toBe('wechat')
  })

  it('enabled with no binding still appears for bind action', () => {
    const rows = mergeBindingRows([{ code: 'dingtalk', displayName: '钉钉' }], [])
    expect(rows).toHaveLength(1)
    expect(rows[0].binding).toBeUndefined()
    expect(rows[0].enabled).toBe(true)
  })
})
