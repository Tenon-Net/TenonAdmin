import { describe, it, expect } from 'vitest'
import { blankForm, rowToInput } from './positionForm'
import type { SysPosition } from '@/types/api'

describe('blankForm', () => {
  it('默认启用、sort 0、名/码空串', () => {
    expect(blankForm()).toEqual({ name: '', code: '', sort: 0, enabled: true })
  })
})

describe('rowToInput', () => {
  it('全量映射四字段,丢掉 id/createTime(全量替换语义下漏字段会抹空该行)', () => {
    const row: SysPosition = { id: 9, name: 'a', code: 'b', sort: 3, enabled: false, createTime: '2026-01-01' }
    expect(rowToInput(row)).toEqual({ name: 'a', code: 'b', sort: 3, enabled: false })
  })
})
