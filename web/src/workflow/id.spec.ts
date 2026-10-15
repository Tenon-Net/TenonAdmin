import { describe, expect, it } from 'vitest'
import { isPositiveWfId, normalizeWfId, wfIdEquals, wfInstanceDetailPath, workflowPathId } from './id'

describe('workflow id', () => {
  it('接受安全正整数并拒绝不安全或非正数', () => {
    expect(normalizeWfId(42)).toBe(42)
    expect(normalizeWfId(0)).toBeNull()
    expect(normalizeWfId(Number.MAX_SAFE_INTEGER + 1)).toBeNull()
  })

  it('保留正十进制字符串，包括超过 MAX_SAFE_INTEGER 的雪花 Id', () => {
    const id = '9007199254740993'
    expect(normalizeWfId(id)).toBe(id)
    expect(isPositiveWfId(id)).toBe(true)
    expect(workflowPathId(id) as unknown).toBe(id)
    expect(normalizeWfId('0')).toBeNull()
  })

  it('按规范字符串比较 number 与 string', () => {
    expect(wfIdEquals(42, '42')).toBe(true)
    expect(wfIdEquals('042', 42)).toBe(false)
    expect(wfIdEquals(null, null)).toBe(false)
  })
})

// #56:「查看」导航也走 normalizeWfId,精度已丢失时不生成路由。
describe('wfInstanceDetailPath', () => {
  it('安全 number 与十进制字符串原样生成详情路由', () => {
    expect(wfInstanceDetailPath(859698005336064)).toBe('/workflow/instance/859698005336064/detail')
    expect(wfInstanceDetailPath('9007199254741011')).toBe('/workflow/instance/9007199254741011/detail')
  })
  it('超出安全整数(已丢精度)或非法值返回 null', () => {
    expect(wfInstanceDetailPath(9007199254741012)).toBeNull()
    expect(wfInstanceDetailPath(0)).toBeNull()
    expect(wfInstanceDetailPath(null)).toBeNull()
  })
})
