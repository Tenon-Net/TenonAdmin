import { describe, it, expect } from 'vitest'
import { isHttpUrl } from './url'

describe('isHttpUrl', () => {
  it('识别 http(s) 绝对 URL', () => {
    expect(isHttpUrl('https://example.com')).toBe(true)
    expect(isHttpUrl('http://a.b/c?d=1')).toBe(true)
    expect(isHttpUrl('HTTPS://X.Y')).toBe(true)
  })
  it('内部路径 / 空值不算 URL', () => {
    expect(isHttpUrl('/system/user')).toBe(false)
    expect(isHttpUrl('system/user/index')).toBe(false)
    expect(isHttpUrl('')).toBe(false)
    expect(isHttpUrl(undefined)).toBe(false)
    expect(isHttpUrl(null)).toBe(false)
    // 协议相对/其他协议不接受(iframe/外链需明确 http(s))
    expect(isHttpUrl('//example.com')).toBe(false)
    expect(isHttpUrl('ftp://x')).toBe(false)
  })
})
