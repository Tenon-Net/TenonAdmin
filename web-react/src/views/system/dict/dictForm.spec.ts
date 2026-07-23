import { describe, it, expect } from 'vitest'
import type { SysDictItem, SysDictType } from '@/types/api'
import { blankItem, blankType, itemToInput, typeToInput } from './dictForm'

const TYPE: SysDictType = { id: 1, code: 'sex', name: '性别', sort: 3, enabled: true, remark: null }
const ITEM: SysDictItem = { id: 11, dictTypeCode: 'sex', label: '男', value: '1', sort: 2, enabled: false }

describe('typeToInput', () => {
  it('全量映射每字段(remark 归一空串)', () => {
    expect(typeToInput(TYPE)).toEqual({ code: 'sex', name: '性别', sort: 3, enabled: true, remark: '' })
  })
  it('remark 有值时保留', () => {
    expect(typeToInput({ ...TYPE, remark: '备注' }).remark).toBe('备注')
  })
})

describe('itemToInput', () => {
  it('全量映射每字段(含 dictTypeCode)', () => {
    expect(itemToInput(ITEM)).toEqual({ dictTypeCode: 'sex', label: '男', value: '1', sort: 2, enabled: false })
  })
})

describe('blankType', () => {
  it('默认:空 code/name、sort 0、启用、空 remark', () => {
    expect(blankType()).toEqual({ code: '', name: '', sort: 0, enabled: true, remark: '' })
  })
})

describe('blankItem', () => {
  it('默认 dictTypeCode 空;传入则带上', () => {
    expect(blankItem()).toEqual({ dictTypeCode: '', label: '', value: '', sort: 0, enabled: true })
    expect(blankItem('sex').dictTypeCode).toBe('sex')
  })
})
