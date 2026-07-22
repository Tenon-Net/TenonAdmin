import { describe, it, expect } from 'vitest'
import { buildLineOption, buildPieOption, fmtTime, num, visibleLeaves } from './chartOptions'
import type { MenuNode } from '@/types/menu'
import { MenuType } from '@/types/menu'

// EChartsOption 是大联合,断言时取 any 避免类型体操
const opt = (o: unknown) => o as any

describe('buildLineOption', () => {
  it('单序列不出图例、多序列出图例', () => {
    expect(opt(buildLineOption([], [{ name: 'a', data: [1] }])).legend).toBeUndefined()
    expect(opt(buildLineOption([], [{ name: 'a', data: [1] }, { name: 'b', data: [2] }])).legend).toEqual({})
  })
  it('categories 进 xAxis、boundaryGap false、series 全 line+smooth', () => {
    const o = opt(buildLineOption(['d1', 'd2'], [{ name: '登录', data: [3, 4] }]))
    expect(o.xAxis.data).toEqual(['d1', 'd2'])
    expect(o.xAxis.boundaryGap).toBe(false)
    expect(o.series[0]).toMatchObject({ name: '登录', type: 'line', smooth: true, data: [3, 4] })
  })
})

describe('buildPieOption', () => {
  it('环形饼:type pie、ring radius、data 透传', () => {
    const data = [{ name: 'x', value: 1 }]
    const o = opt(buildPieOption(data))
    expect(o.series[0].type).toBe('pie')
    expect(o.series[0].radius).toEqual(['42%', '68%'])
    expect(o.series[0].data).toBe(data)
    expect(o.tooltip).toEqual({ trigger: 'item' })
  })
})

describe('num', () => {
  it('undefined → —', () => expect(num(undefined)).toBe('—'))
  it('0 保留(不当缺失)', () => expect(num(0)).toBe(0))
  it('正数原样', () => expect(num(7)).toBe(7))
})

describe('visibleLeaves', () => {
  const leaf = (id: number, path: string | undefined, visible = true, children: MenuNode[] = []): MenuNode => ({
    id, parentId: 0, type: children.length ? MenuType.Catalog : MenuType.Menu, title: 't' + id, path, sort: 0, visible, children,
  })
  it('收集可见带 path 叶子;不可见整枝跳过;无 path 叶子跳过', () => {
    const tree = [
      leaf(1, '/a'),
      leaf(2, '/b', false), // 不可见 → 跳
      leaf(3, undefined), // 无 path → 跳
      leaf(4, undefined, true, [leaf(5, '/c'), leaf(6, '/d', false)]), // 目录:下钻,/c 进、/d 不可见跳
    ]
    expect(visibleLeaves(tree).map((n) => n.path)).toEqual(['/a', '/c'])
  })
  it('excludePath 排除当前页自身;无 path 叶子即便 excludePath 有值也跳过', () => {
    // 后半防 `n.path &&` 真值守卫被误删:excludePath 有值时,无 path 叶子的 `undefined !== '/self'` 会为真而漏进,
    // 变成 navigate(undefined) 崩的快捷入口。excludePath 缺省时该缺陷不可观测(undefined !== undefined 为假)。
    const tree = [leaf(1, '/a'), leaf(2, '/self'), leaf(3, undefined)]
    expect(visibleLeaves(tree, '/self').map((n) => n.path)).toEqual(['/a'])
  })
  it('不可见目录整枝不下钻', () => {
    const tree = [leaf(1, undefined, false, [leaf(2, '/deep')])]
    expect(visibleLeaves(tree)).toEqual([])
  })
})

describe('fmtTime', () => {
  it('去 T 截到分', () => expect(fmtTime('2026-07-21T08:15:30')).toBe('2026-07-21 08:15'))
  it('null/undefined → 空串', () => {
    expect(fmtTime(null)).toBe('')
    expect(fmtTime(undefined)).toBe('')
  })
})
