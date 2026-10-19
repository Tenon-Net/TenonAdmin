import { describe, expect, it } from 'vitest'
import { parseQueryApiId } from './id'

describe('parseQueryApiId', () => {
  it('为任务和用户筛选把安全正整数恢复成 number，以匹配 API 下拉选项', () => {
    expect(parseQueryApiId('42')).toBe(42)
  })

  it('为工作流预选保留超过安全范围的十进制字符串', () => {
    expect(parseQueryApiId('9007199254740993')).toBe('9007199254740993')
  })

  it('拒绝零、非十进制值和数组查询参数', () => {
    expect(parseQueryApiId('0')).toBeUndefined()
    expect(parseQueryApiId('42x')).toBeUndefined()
    expect(parseQueryApiId(['42'])).toBeUndefined()
  })
})
