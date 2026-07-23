import { describe, it, expect } from 'vitest'
import type { ModuleRow } from '@/types/api'
import { blankModule, isBuiltin, moduleToInput } from './moduleForm'

const ROW: ModuleRow = { id: 2, code: 'biz', title: '业务', icon: 'ph:cube', defaultRoute: '/biz', apiPrefix: 'biz', sort: 1, enabled: false, remark: 'r' }

describe('isBuiltin', () => {
  it('code=system 为内置,其余否', () => {
    expect(isBuiltin({ code: 'system' })).toBe(true)
    expect(isBuiltin({ code: 'biz' })).toBe(false)
  })
})

describe('moduleToInput', () => {
  it('全量映射每字段', () => {
    expect(moduleToInput(ROW)).toEqual({ code: 'biz', title: '业务', icon: 'ph:cube', defaultRoute: '/biz', apiPrefix: 'biz', sort: 1, enabled: false, remark: 'r' })
  })
  it('可空字段归一空串', () => {
    const r = moduleToInput({ ...ROW, icon: null, defaultRoute: null, apiPrefix: null, remark: null })
    expect(r.icon).toBe(''); expect(r.defaultRoute).toBe(''); expect(r.apiPrefix).toBe(''); expect(r.remark).toBe('')
  })
})

describe('blankModule', () => {
  it('默认:全空串 / sort 0 / 启用', () => {
    expect(blankModule()).toEqual({ code: '', title: '', icon: '', defaultRoute: '', apiPrefix: '', sort: 0, enabled: true, remark: '' })
  })
})
