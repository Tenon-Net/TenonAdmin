import { describe, expect, it } from 'vitest'
import { parseCustomOrgIds } from './scopeIds'

describe('parseCustomOrgIds', () => {
  it('安全 Id 还原为 number，大 Id 保留十进制字符串', () => {
    expect(parseCustomOrgIds('42,9007199254740993')).toEqual([42, '9007199254740993'])
  })
})
