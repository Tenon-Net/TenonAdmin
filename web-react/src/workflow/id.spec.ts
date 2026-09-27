import { describe, expect, it } from 'vitest'
import { isPositiveWfId, normalizeWfId, wfIdEquals } from './id'

describe('workflow id', () => {
  it('接受安全正整数并拒绝不安全或非正数', () => {
    expect(normalizeWfId(42)).toBe(42)
    expect(normalizeWfId(0)).toBeNull()
    expect(normalizeWfId(Number.MAX_SAFE_INTEGER + 1)).toBeNull()
  })

  it('保留正十进制字符串，包括 19 位雪花 Id', () => {
    const id = '9223372036854775807'
    expect(normalizeWfId(id)).toBe(id)
    expect(isPositiveWfId(id)).toBe(true)
    expect(normalizeWfId('0')).toBeNull()
    expect(normalizeWfId(' 42 ')).toBeNull()
  })

  it('按规范字符串比较 number 与 string', () => {
    expect(wfIdEquals(42, '42')).toBe(true)
    expect(wfIdEquals('042', 42)).toBe(false)
    expect(wfIdEquals(null, null)).toBe(false)
  })
})
