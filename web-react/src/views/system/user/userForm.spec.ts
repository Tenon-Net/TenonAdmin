import { describe, it, expect } from 'vitest'
import {
  blankForm, canDelete, canEdit, canReset, canToggleEnabled, detailToForm, toAddInput, toUpdateInput,
  type UserForm,
} from './userForm'
import type { UserDetail } from '@/types/api'

const filled: UserForm = {
  account: 'alice', password: 's3cret', name: 'Alice', nickname: 'A', phone: '13800000000', email: 'a@b.com',
  gender: 'M', enabled: true, roleIds: [1, 2],
  orgId: 7, positionId: 3, directorId: 9, avatar: 'a.png',
}

describe('toAddInput 空值语义', () => {
  it('password 留空 → 省略字段(undefined,非 null):否则后端把 null 当"设空口令",建出登不进去的号', () => {
    const r = toAddInput({ ...filled, password: '' })
    expect(r.password).toBeUndefined()
    expect(r.password).not.toBeNull() // 钉死 `|| undefined` 被改成 `|| null`
  })
  it('password 有值 → 原样带上', () => {
    expect(toAddInput({ ...filled, password: 'x' }).password).toBe('x')
  })
  it('可空文本留空 → null(显式置空),不是 undefined', () => {
    const r = toAddInput({ ...filled, nickname: '', phone: '', email: '' })
    expect(r.nickname).toBeNull()
    expect(r.phone).toBeNull()
    expect(r.email).toBeNull()
    expect(r.nickname).not.toBeUndefined() // 钉死 nickname 被改成 `|| undefined`
  })
  it('roleIds / enabled / gender 原样透传', () => {
    const r = toAddInput({ ...filled, roleIds: [5, 6], enabled: false, gender: 'F' })
    expect(r.roleIds).toEqual([5, 6])
    expect(r.enabled).toBe(false)
    expect(r.gender).toBe('F')
  })
})

describe('toUpdateInput 全量替换:透传字段不能丢', () => {
  it('orgId/positionId/directorId/avatar 原样回送(本页无编辑控件但必须带回,否则被后端置空 = 数据丢失)', () => {
    const r = toUpdateInput(filled)
    expect(r.orgId).toBe(7)
    expect(r.positionId).toBe(3)
    expect(r.directorId).toBe(9)
    expect(r.avatar).toBe('a.png')
  })
  it('不含 account/password(更新入参无此两项)', () => {
    const r = toUpdateInput(filled) as unknown as Record<string, unknown>
    expect('account' in r).toBe(false)
    expect('password' in r).toBe(false)
  })
})

describe('detailToForm 回显', () => {
  const detail: UserDetail = {
    id: 1, account: 'bob', name: 'Bob', nickname: null, phone: null, email: null,
    gender: null, avatar: 'x.png', orgId: 5, positionId: null, directorId: null,
    enabled: false, isSuperAdmin: false, roleIds: [3], createTime: '2026-01-01',
  }
  it('可空文本 null → 空串(绑 input);password 永远空串;透传字段带出', () => {
    const f = detailToForm(detail)
    expect(f.password).toBe('')
    expect(f.nickname).toBe('')
    expect(f.phone).toBe('')
    expect(f.gender).toBeNull() // gender 保持可空(select 而非 input)
    expect(f.avatar).toBe('x.png')
    expect(f.orgId).toBe(5)
    expect(f.roleIds).toEqual([3])
    expect(f.enabled).toBe(false)
  })
  it('roleIds 缺省 → 空数组(不是 undefined)', () => {
    const f = detailToForm({ ...detail, roleIds: undefined as unknown as number[] })
    expect(f.roleIds).toEqual([])
  })
})

describe('行内动作判据:超管自锁保护', () => {
  const yes = () => true
  const no = () => false
  it('canDelete:超管一律 false(即便有删除权限),普通用户看权限码', () => {
    expect(canDelete({ isSuperAdmin: true }, yes)).toBe(false)
    expect(canDelete({ isSuperAdmin: false }, yes)).toBe(true)
    expect(canDelete({ isSuperAdmin: false }, no)).toBe(false)
  })
  it('canToggleEnabled:超管一律 false(防停用自锁),普通用户看权限码', () => {
    expect(canToggleEnabled({ isSuperAdmin: true }, yes)).toBe(false)
    expect(canToggleEnabled({ isSuperAdmin: false }, yes)).toBe(true)
    expect(canToggleEnabled({ isSuperAdmin: false }, no)).toBe(false)
  })
  it('canEdit / canReset:超管可编辑/可重置(只看权限码,不受自锁保护)', () => {
    expect(canEdit({ isSuperAdmin: true }, yes)).toBe(true)
    expect(canReset({ isSuperAdmin: true }, yes)).toBe(true)
    expect(canEdit({ isSuperAdmin: false }, no)).toBe(false)
    expect(canReset({ isSuperAdmin: false }, no)).toBe(false)
  })
})

describe('blankForm', () => {
  it('默认 enabled=true、空角色、透传字段全 null', () => {
    const b = blankForm()
    expect(b.enabled).toBe(true)
    expect(b.roleIds).toEqual([])
    expect(b.orgId).toBeNull()
    expect(b.avatar).toBeNull()
    expect(b.password).toBe('')
  })
})
