import { describe, it, expect } from 'vitest'
import { parseUserAgent, uaSummary } from './ua'

const EDGE_WIN = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36 Edg/120'
const CHROME_MAC = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 Chrome/120 Safari/537.36'

describe('parseUserAgent', () => {
  it('Edge 优先于 Chrome(UA 同时含 Chrome 标记)+ Windows NT 10 → Windows 10/11', () => {
    // 顺序敏感的核心:Edge/Opera 的 UA 都夹带 Chrome 标记,排在 Chrome 前才不会被误判成 Chrome。
    expect(parseUserAgent(EDGE_WIN)).toEqual({ browser: 'Edge', os: 'Windows 10/11' })
  })
  it('空/null 返回空字段', () => {
    expect(parseUserAgent(null)).toEqual({ browser: '', os: '' })
    expect(parseUserAgent('')).toEqual({ browser: '', os: '' })
  })
})

describe('uaSummary', () => {
  it('浏览器 · 系统(中点分隔)', () => {
    expect(uaSummary(CHROME_MAC)).toBe('Chrome · macOS')
  })
  it('只识别到浏览器时不带分隔符(空字段被 filter 掉)', () => {
    expect(uaSummary('Chrome/120')).toBe('Chrome')
  })
  it('全未识别回落原始 UA 串', () => {
    expect(uaSummary('SomeRandomBot/1.0')).toBe('SomeRandomBot/1.0')
  })
  it('空 UA 回落占位符 —', () => {
    expect(uaSummary(null)).toBe('—')
    expect(uaSummary('')).toBe('—')
  })
})
