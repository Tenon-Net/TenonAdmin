import { describe, it, expect, afterEach } from 'vitest'
import { render, cleanup } from '@testing-library/react'
import { createElement } from 'react'
import { svgToIcon, sortedIconNames, getLocalIconNames, loadIconNames } from '@/lib/icons'
import { AppIcon } from '@/components/AppIcon'

afterEach(cleanup)

type CollJSON = Parameters<typeof sortedIconNames>[0]
const coll = (names: string[]): CollJSON =>
  ({ prefix: 'x', icons: Object.fromEntries(names.map((n) => [n, { body: '' }])) }) as unknown as CollJSON

// ── svgToIcon(纯逻辑,变异钉死)──
describe('svgToIcon', () => {
  it('取 viewBox 宽高 + 剥外层 svg 留内容体', () => {
    const raw = '<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg"><path d="M0"/></svg>'
    expect(svgToIcon(raw)).toEqual({ body: '<path d="M0"/>', width: 24, height: 24 })
  })
  it('非 24 的 viewBox:宽高各取第 3/4 段', () => {
    const r = svgToIcon('<svg viewBox="0 0 48 32"><g/></svg>')
    expect([r.width, r.height]).toEqual([48, 32])
  })
  it('无 viewBox → 兜底 24×24', () => {
    const r = svgToIcon('<svg><path/></svg>')
    expect([r.width, r.height]).toEqual([24, 24])
  })
})

// ── sortedIconNames(纯逻辑)──
describe('sortedIconNames', () => {
  it('取键并按字典序排', () => {
    expect(sortedIconNames(coll(['banana', 'apple', 'cherry']))).toEqual(['apple', 'banana', 'cherry'])
  })
  it('无 icons 键 → 空数组(?? {} 兜住,不抛)', () => {
    expect(sortedIconNames({ prefix: 'x' } as unknown as CollJSON)).toEqual([])
  })
})

// ── 本地 svg 注册(模块级 eager glob → addIcon)──
describe('本地 svg', () => {
  it('getLocalIconNames 含 star(src/assets/svg/star.svg)', () => {
    expect(getLocalIconNames()).toContain('star')
  })
  it('local:star 已注册,AppIcon 渲成 svg', () => {
    const { container } = render(createElement(AppIcon, { icon: 'local:star' }))
    expect(container.querySelector('svg')).toBeTruthy()
  })
})

// ── loadIconNames 未知前缀短路 ──
describe('loadIconNames', () => {
  it('未知前缀 → 空数组(不 import、不 addCollection)', async () => {
    await expect(loadIconNames('nosuchprefix')).resolves.toEqual([])
  })
})
