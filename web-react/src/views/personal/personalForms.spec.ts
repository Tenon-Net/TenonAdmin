import { describe, it, expect } from 'vitest'
import { fmtDateTime, formToUpdate, initial, isEmail, isPhone, profileToForm } from './personalForms'
import type { UserProfile } from '@/types/api'

describe('initial', () => {
  it('取首字母大写', () => expect(initial('alice')).toBe('A'))
  it('去空白后取首', () => expect(initial('  bob')).toBe('B'))
  it('空名回退 ?', () => expect(initial('')).toBe('?'))
  it('中文首字', () => expect(initial('张三')).toBe('张'))
})

describe('isPhone(选填)', () => {
  it('空 → 通过(选填)', () => expect(isPhone('')).toBe(true))
  it('合法手机号', () => expect(isPhone('13800138000')).toBe(true))
  it('位数不足', () => expect(isPhone('12345')).toBe(false))
  it('含非数字', () => expect(isPhone('1380013800a')).toBe(false))
  it('非 1 开头', () => expect(isPhone('23800138000')).toBe(false))
})

describe('isEmail(选填)', () => {
  it('空 → 通过', () => expect(isEmail('')).toBe(true))
  it('合法邮箱', () => expect(isEmail('a@b.com')).toBe(true))
  it('无 @', () => expect(isEmail('bad')).toBe(false))
  it('无域名点', () => expect(isEmail('a@b')).toBe(false))
})

describe('fmtDateTime', () => {
  it('去 T 截到秒', () => expect(fmtDateTime('2026-07-21T08:15:30')).toBe('2026-07-21 08:15:30'))
  it('null/undefined → 空串', () => {
    expect(fmtDateTime(null)).toBe('')
    expect(fmtDateTime(undefined)).toBe('')
  })
})

describe('profileToForm', () => {
  it('全字段映射;选填 null→空串,gender/avatar 保 null,org/position 名回落 —', () => {
    const p: UserProfile = { id: 1, account: 'acc', name: '张三', isSuperAdmin: false, nickname: null, phone: null, email: null, gender: null, avatar: null, orgName: null, positionName: null }
    expect(profileToForm(p)).toEqual({ account: 'acc', name: '张三', nickname: '', phone: '', email: '', gender: null, avatar: null, org: '—', position: '—' })
  })
  it('有值时透传', () => {
    const p: UserProfile = { id: 1, account: 'acc', name: 'N', isSuperAdmin: false, nickname: '昵', phone: '138', email: 'a@b.com', gender: '1', avatar: 'u', orgName: '研发', positionName: '工程师' }
    expect(profileToForm(p)).toMatchObject({ nickname: '昵', phone: '138', email: 'a@b.com', gender: '1', avatar: 'u', org: '研发', position: '工程师' })
  })
})

describe('formToUpdate', () => {
  it('选填空串归一 null;name/gender/avatar 原样', () => {
    expect(formToUpdate({ name: 'N', nickname: '', phone: '', email: '', gender: null, avatar: null })).toEqual({
      name: 'N', nickname: null, phone: null, email: null, gender: null, avatar: null,
    })
  })
  it('有值保留', () => {
    expect(formToUpdate({ name: 'N', nickname: '昵', phone: '138', email: 'a@b.com', gender: '2', avatar: 'u' })).toEqual({
      name: 'N', nickname: '昵', phone: '138', email: 'a@b.com', gender: '2', avatar: 'u',
    })
  })
})
