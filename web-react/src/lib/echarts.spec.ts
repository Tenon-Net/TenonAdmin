import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { buildEChartsTheme } from './echarts'

// buildEChartsTheme 现读 getComputedStyle(document.documentElement) 的 CSS 变量;
// 在 root 上 inline 设一套已知值,断主题对象把它们接到对的位置(单一色源 = tokens)。
const root = document.documentElement
const VARS: Record<string, string> = {
  '--color-primary': '#111111',
  '--color-success': '#222222',
  '--color-warning': '#333333',
  '--color-danger': '#444444',
  '--color-info': '#555555',
  '--color-text-secondary': '#666666',
  '--color-text-primary': '#777777',
  '--color-border': '#888888',
}

beforeEach(() => {
  for (const [k, v] of Object.entries(VARS)) root.style.setProperty(k, v)
})
afterEach(() => {
  for (const k of Object.keys(VARS)) root.style.removeProperty(k)
})

describe('buildEChartsTheme', () => {
  it('色板取自 CSS 变量(5 语义色 + 末位固定紫)', () => {
    expect(buildEChartsTheme().color).toEqual(['#111111', '#222222', '#333333', '#444444', '#555555', '#7C5CFF'])
  })
  it('文字/标题/轴线各接对应变量', () => {
    const t = buildEChartsTheme()
    expect(t.textStyle.color).toBe('#666666') // text-secondary
    expect(t.title.textStyle.color).toBe('#777777') // text-primary
    expect(t.categoryAxis.axisLine.lineStyle.color).toBe('#888888') // border
    expect(t.categoryAxis.axisLabel.color).toBe('#666666') // text-secondary
    expect(t.backgroundColor).toBe('transparent')
  })
})
