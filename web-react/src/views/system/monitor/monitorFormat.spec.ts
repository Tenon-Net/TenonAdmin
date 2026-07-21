import { describe, it, expect } from 'vitest'
import { fmtBytes, uptimeParts, pct, usageColor } from './monitorFormat'

describe('fmtBytes', () => {
  it('二进制分档 + 边界:0 回 0 B、非 B 档保留 2 位、超 TB 封顶', () => {
    expect(fmtBytes(0)).toBe('0 B') // ≤0 守卫(否则 log(0) 崩成 NaN undefined)
    expect(fmtBytes(-5)).toBe('0 B')
    expect(fmtBytes(512)).toBe('512 B') // B 档 0 位小数
    expect(fmtBytes(1536)).toBe('1.50 KB') // 非 B 档 2 位小数
    expect(fmtBytes(1024 ** 3)).toBe('1.00 GB')
    expect(fmtBytes(1024 ** 5)).toBe('1024.00 TB') // 超 TB 封顶在 TB(不越界成 undefined)
  })
})

describe('uptimeParts', () => {
  it('秒 → {天,时,分} 分解', () => {
    // 90000s = 1天 1时 0分;25小时会暴露 days 用错除数、hours 用错模/除数
    expect(uptimeParts(90000)).toEqual({ days: 1, hours: 1, minutes: 0 })
    expect(uptimeParts(3661)).toEqual({ days: 0, hours: 1, minutes: 1 })
  })
})

describe('pct', () => {
  it('四舍五入整数;total≤0 回 0(防除零)', () => {
    expect(pct(2, 3)).toBe(67) // round(66.67)=67(floor 会得 66)
    expect(pct(1, 4)).toBe(25)
    expect(pct(5, 0)).toBe(0) // 除零守卫
  })
})

describe('usageColor', () => {
  it('阈值染色(边界含 90/75)', () => {
    expect(usageColor(90)).toBe('var(--color-danger)') // ≥90 边界
    expect(usageColor(89)).toBe('var(--color-warning)')
    expect(usageColor(75)).toBe('var(--color-warning)') // ≥75 边界
    expect(usageColor(74)).toBe('var(--color-success)')
  })
})
