import { describe, it, expect } from 'vitest'
import { DuplicateStrategy, type ImportRow } from '@/types/api'
import { IMPORT_DUPLICATE_IN_DB, hardErrorsOf, isDuplicateOnly, isHardError } from './importDup'

const DUP = { columnKey: 'Account', code: IMPORT_DUPLICATE_IN_DB }
const REQUIRED = { columnKey: 'Name', code: 46005 }

function row(...errors: ImportRow['errors']): ImportRow {
  return { index: 1, cells: { Account: 'a1', Name: 'n' }, errors }
}

describe('importDup', () => {
  it('「已存在」在跳过/覆盖策略下不算硬错误,Error 策略下才算', () => {
    expect(isHardError(DUP, DuplicateStrategy.Skip)).toBe(false)
    expect(isHardError(DUP, DuplicateStrategy.Overwrite)).toBe(false)
    expect(isHardError(DUP, DuplicateStrategy.Error)).toBe(true)
  })

  it('其他码在任何策略下都是硬错误', () => {
    for (const s of [DuplicateStrategy.Skip, DuplicateStrategy.Overwrite, DuplicateStrategy.Error])
      expect(isHardError(REQUIRED, s)).toBe(true)
  })

  it('hardErrorsOf 在跳过策略下滤掉「已存在」,保留真错误', () => {
    expect(hardErrorsOf(row(DUP), DuplicateStrategy.Skip)).toEqual([])
    expect(hardErrorsOf(row(DUP, REQUIRED), DuplicateStrategy.Skip)).toEqual([REQUIRED])
    expect(hardErrorsOf(row(DUP), DuplicateStrategy.Error)).toEqual([DUP])
  })

  it('isDuplicateOnly:只有「已存在」才算,掺了真错误就不算', () => {
    expect(isDuplicateOnly(row(DUP), DuplicateStrategy.Skip)).toBe(true)
    expect(isDuplicateOnly(row(DUP), DuplicateStrategy.Overwrite)).toBe(true)
    // Error 策略下「已存在」本身就是要用户处理的错误,不该再显示成「将按策略处理」
    expect(isDuplicateOnly(row(DUP), DuplicateStrategy.Error)).toBe(false)
    expect(isDuplicateOnly(row(DUP, REQUIRED), DuplicateStrategy.Skip)).toBe(false)
    expect(isDuplicateOnly(row(), DuplicateStrategy.Skip)).toBe(false)
    expect(isDuplicateOnly(row(REQUIRED), DuplicateStrategy.Skip)).toBe(false)
  })
})
