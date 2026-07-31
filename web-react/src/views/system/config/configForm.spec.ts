import { describe, it, expect } from 'vitest'
import {
  ALL_BOOL_KEYS, CAPTCHA_KEY, CAPTCHA_TYPE_KEY, KEY_ALLOWED, KEY_MAX_SIZE, NUM_FIELDS,
  RATELIMIT_KEY, SMS_LOGIN_KEY, SMS_MFA_KEY, SYS_FIELDS, TOTP_KEY,
  blankConfig, configToInput, normalizeExt, parseBase, parseSecurity, parseUpload,
  rowsToMap, serializeBase, serializeSecurity, serializeUpload, type SecurityState,
} from './configForm'
import type { SysConfig } from '@/types/api'

describe('rowsToMap', () => {
  it('null/undefined 值归一空串;按 key 建映射', () => {
    const m = rowsToMap([{ configKey: 'a', configValue: 'x' }, { configKey: 'b', configValue: null }])
    expect(m.get('a')).toBe('x')
    expect(m.get('b')).toBe('')
    expect(m.get('missing')).toBeUndefined()
  })
})

describe('系统基础 parse/serialize', () => {
  it('parseBase:命中回填、缺失归空串,只认 SYS_FIELDS', () => {
    const v = parseBase([{ configKey: 'sys.site.title', configValue: '榫卯' }, { configKey: 'noise', configValue: 'z' }])
    expect(v['sys.site.title']).toBe('榫卯')
    expect(v['sys.site.logo']).toBe('') // 缺失 → 空串
    expect(v).not.toHaveProperty('noise') // 非 SYS_FIELDS 不进
    expect(Object.keys(v)).toHaveLength(SYS_FIELDS.length)
  })
  it('serializeBase:全字段回写,缺失键值为空串', () => {
    const items = serializeBase({ 'sys.site.title': 'T' })
    expect(items).toHaveLength(SYS_FIELDS.length)
    expect(items.find((i) => i.configKey === 'sys.site.title')?.configValue).toBe('T')
    expect(items.find((i) => i.configKey === 'sys.site.logo')?.configValue).toBe('')
  })
})

describe('normalizeExt', () => {
  it('补前导点', () => expect(normalizeExt('jpg')).toBe('.jpg'))
  it('已有点保留', () => expect(normalizeExt('.png')).toBe('.png'))
  it('转小写', () => expect(normalizeExt('JPG')).toBe('.jpg'))
  it('去空白后判断', () => expect(normalizeExt('  .Gif  ')).toBe('.gif'))
  it('已有点 + 大写', () => expect(normalizeExt('.PDF')).toBe('.pdf'))
})

describe('上传 parse/serialize', () => {
  it('parseUpload:数值 + 后缀拆分去空过滤', () => {
    const u = parseUpload([{ configKey: KEY_MAX_SIZE, configValue: '50' }, { configKey: KEY_ALLOWED, configValue: '.jpg, .png ,, .gif' }])
    expect(u.maxSizeMb).toBe(50)
    expect(u.exts).toEqual(['.jpg', '.png', '.gif'])
  })
  it('parseUpload:缺失/空/0 → 默认 20、空数组', () => {
    expect(parseUpload([]).maxSizeMb).toBe(20)
    expect(parseUpload([]).exts).toEqual([])
    expect(parseUpload([{ configKey: KEY_MAX_SIZE, configValue: '0' }]).maxSizeMb).toBe(20) // 0 falsy → 20
  })
  it('serializeUpload:数值转串、后缀 normalize + 逗号连接', () => {
    const items = serializeUpload(30, ['JPG', '.png'])
    expect(items.find((i) => i.configKey === KEY_MAX_SIZE)?.configValue).toBe('30')
    expect(items.find((i) => i.configKey === KEY_ALLOWED)?.configValue).toBe('.jpg,.png')
  })
})

describe('安全策略 parse/serialize', () => {
  const rows: { configKey: string; configValue: string | null }[] = [
    { configKey: 'sys.security.loginLock.maxFailCount', configValue: '3' },
    { configKey: 'sys.security.loginLock.lockMinutes', configValue: '0' }, // min 1 → 0 无效回退 1
    { configKey: 'sys.security.password.expireDays', configValue: '0' }, // min 0 → 保留 0
    { configKey: 'sys.security.password.requireUpper', configValue: 'true' },
    { configKey: 'sys.security.password.requireLower', configValue: 'false' },
    { configKey: CAPTCHA_KEY, configValue: 'true' },
    { configKey: CAPTCHA_TYPE_KEY, configValue: 'math' },
    { configKey: SMS_MFA_KEY, configValue: 'true' },
    { configKey: RATELIMIT_KEY, configValue: 'true' },
  ]
  it('parseSecurity:数值 min 兜底、布尔 true 串、captchaType', () => {
    const s = parseSecurity(rows)
    expect(s.nums['sys.security.loginLock.maxFailCount']).toBe(3)
    expect(s.nums['sys.security.loginLock.lockMinutes']).toBe(1) // 0 → min 1
    expect(s.nums['sys.security.password.expireDays']).toBe(0) // min 0 保留
    expect(s.nums['sys.security.session.accessMinutes']).toBe(1) // 缺失 → min 1
    expect(s.bools['sys.security.password.requireUpper']).toBe(true)
    expect(s.bools['sys.security.password.requireLower']).toBe(false)
    expect(s.bools['sys.security.password.requireDigit']).toBe(false) // 缺失 → false
    expect(s.bools[CAPTCHA_KEY]).toBe(true)
    expect(s.captchaType).toBe('math')
  })
  it('parseSecurity:captchaType 缺失 → char', () => {
    expect(parseSecurity([]).captchaType).toBe('char')
  })
  it('serializeSecurity:数值/布尔转串,键集含全部 NUM + BOOL + captchaType', () => {
    const s = parseSecurity(rows)
    const items = serializeSecurity(s)
    const map = new Map(items.map((i) => [i.configKey, i.configValue]))
    expect(map.get('sys.security.loginLock.maxFailCount')).toBe('3')
    expect(map.get('sys.security.password.requireUpper')).toBe('true')
    expect(map.get('sys.security.password.requireLower')).toBe('false')
    expect(map.get(CAPTCHA_TYPE_KEY)).toBe('math')
    // 键集完整:NUM + BOOL(含 TOTP) + 1 captchaType
    expect(items).toHaveLength(NUM_FIELDS.length + ALL_BOOL_KEYS.length + 1)
    for (const k of ALL_BOOL_KEYS) expect(map.has(k)).toBe(true)
  })
  it('round-trip:serialize→parse 还原(全键在场时)', () => {
    const state: SecurityState = {
      nums: Object.fromEntries(NUM_FIELDS.map((f) => [f.key, f.min + 5])),
      bools: Object.fromEntries(ALL_BOOL_KEYS.map((k, i) => [k, i % 2 === 0])),
      captchaType: 'path',
    }
    const back = parseSecurity(serializeSecurity(state))
    expect(back).toEqual(state)
  })
  it('ALL_BOOL_KEYS = 密码4 + 验证码 + TOTP2 + mfa + smsLogin + 限流(共10,无重复)', () => {
    expect(ALL_BOOL_KEYS).toHaveLength(10)
    expect(new Set(ALL_BOOL_KEYS).size).toBe(10)
    expect(ALL_BOOL_KEYS).toContain(SMS_LOGIN_KEY)
    expect(ALL_BOOL_KEYS).toContain(TOTP_KEY)
  })
})

describe('其他配置表单映射', () => {
  it('blankConfig:空白默认', () => {
    expect(blankConfig()).toEqual({ configKey: '', configValue: '', name: '', groupCode: '', sort: 0, remark: '' })
  })
  it('configToInput:null 字段归一空串、保留 configKey/sort', () => {
    const r: SysConfig = { id: 9, configKey: 'k', configValue: null, name: 'N', groupCode: null, sort: 5, remark: null }
    expect(configToInput(r)).toEqual({ configKey: 'k', configValue: '', name: 'N', groupCode: '', sort: 5, remark: '' })
  })
})
