import { describe, it, expect } from 'vitest'
import type { SysRole } from '@/types/api'
import { blankRole, roleToInput } from './roleForm'

const ROLE: SysRole = { id: 1, code: 'admin', name: '管理员', sort: 3, enabled: true, remark: null }

describe('roleToInput', () => {
  it('全量映射每字段(remark 归一空串)', () => {
    expect(roleToInput(ROLE)).toEqual({ code: 'admin', name: '管理员', sort: 3, enabled: true, remark: '' })
  })
  it('remark 有值时保留', () => {
    expect(roleToInput({ ...ROLE, remark: '备注' }).remark).toBe('备注')
  })
})

describe('blankRole', () => {
  it('默认:空 name/code、sort 0、启用、空 remark', () => {
    expect(blankRole()).toEqual({ name: '', code: '', sort: 0, enabled: true, remark: '' })
  })
})
