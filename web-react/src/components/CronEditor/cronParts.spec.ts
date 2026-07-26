import { describe, expect, it } from 'vitest'
import { composeSegment, isRestricted, joinCron, parseSegment, setSegment, splitCron } from './cronParts'

describe('splitCron', () => {
  it('6 段原样返回', () => {
    expect(splitCron('0 30 9 * * ?')).toEqual(['0', '30', '9', '*', '*', '?'])
  })
  it('5 段升 6 段(秒位补 0)', () => {
    expect(splitCron('30 9 * * ?')).toEqual(['0', '30', '9', '*', '*', '?'])
  })
  it('空串给默认(秒 0、周 ?)', () => {
    expect(splitCron('  ')).toEqual(['0', '*', '*', '*', '*', '?'])
  })
  it('段数不对返回 null(交表达式页签)', () => {
    expect(splitCron('1 2 3')).toBeNull()
    expect(splitCron('1 2 3 4 5 6 7')).toBeNull()
  })
  it('joinCron 逆操作', () => {
    expect(joinCron(['0', '30', '9', '*', '*', '?'])).toBe('0 30 9 * * ?')
  })
})

describe('parseSegment 通用形态', () => {
  it('* → every;? 仅日/周', () => {
    expect(parseSegment('*', 0)).toEqual({ mode: 'every' })
    expect(parseSegment('?', 3)).toEqual({ mode: 'unspecified' })
    expect(parseSegment('?', 5)).toEqual({ mode: 'unspecified' })
    expect(parseSegment('?', 1)).toBeNull()
  })
  it('区间 / 步长 / 指定值', () => {
    expect(parseSegment('5-10', 1)).toEqual({ mode: 'range', from: 5, to: 10 })
    expect(parseSegment('*/15', 0)).toEqual({ mode: 'step', from: null, step: 15 })
    expect(parseSegment('3/5', 2)).toEqual({ mode: 'step', from: 3, step: 5 })
    expect(parseSegment('1,15,30', 1)).toEqual({ mode: 'values', values: [1, 15, 30] })
    expect(parseSegment('7', 2)).toEqual({ mode: 'values', values: [7] })
  })
  it('周环绕区间照收(后端合法)', () => {
    expect(parseSegment('5-1', 5)).toEqual({ mode: 'range', from: 5, to: 1 })
  })
  it('越界 / 名字 / 混合列表 → null(自定义片段)', () => {
    expect(parseSegment('99', 2)).toBeNull() // 时段 0-23
    expect(parseSegment('MON', 5)).toBeNull() // 名字不进可视化
    expect(parseSegment('1-5,10', 3)).toBeNull() // 混合列表
    expect(parseSegment('32', 3)).toBeNull() // 日段 1-31
    expect(parseSegment('', 0)).toBeNull()
  })
})

describe('parseSegment 日/周专项', () => {
  it('日:L / L-n / nW / LW', () => {
    expect(parseSegment('L', 3)).toEqual({ mode: 'lastDay' })
    expect(parseSegment('L-3', 3)).toEqual({ mode: 'lastOffset', n: 3 })
    expect(parseSegment('15W', 3)).toEqual({ mode: 'nearestWeekday', n: 15 })
    expect(parseSegment('LW', 3)).toEqual({ mode: 'lastWeekday' })
  })
  it('周:nL / n#m;7 归一成 0(周日)', () => {
    expect(parseSegment('5L', 5)).toEqual({ mode: 'lastDow', dow: 5 })
    expect(parseSegment('7L', 5)).toEqual({ mode: 'lastDow', dow: 0 })
    expect(parseSegment('2#3', 5)).toEqual({ mode: 'nthDow', dow: 2, nth: 3 })
    expect(parseSegment('2#6', 5)).toBeNull() // 第 6 个不存在(1..5)
  })
  it('周:>7 的星期值拒收,不 %7 悄悄改写成别的星期', () => {
    // 后端 ParseDowValue 只认 0-7;这里若 %7 兜底,`8L` 会被读成"周一最后一个",
    // 控件显示的与用户写的不是一回事
    expect(parseSegment('8L', 5)).toBeNull()
    expect(parseSegment('9#2', 5)).toBeNull()
  })
  it('专项只在对应段位成立', () => {
    expect(parseSegment('L', 1)).toBeNull()
    expect(parseSegment('5L', 3)).toBeNull()
    expect(parseSegment('15W', 5)).toBeNull()
  })
})

describe('composeSegment(与 parse 互逆)', () => {
  it('全形态往返稳定', () => {
    const cases: Array<[string, 0 | 3 | 5]> = [
      ['*', 0], ['5-10', 0], ['*/15', 0], ['3/5', 0], ['1,15,30', 0],
      ['?', 3], ['L', 3], ['L-3', 3], ['15W', 3], ['LW', 3],
      ['5L', 5], ['2#3', 5],
    ]
    for (const [text, idx] of cases) {
      const state = parseSegment(text, idx)
      expect(state, text).not.toBeNull()
      expect(composeSegment(state!)).toBe(text)
    }
  })
  it('values 排序输出、空退 *', () => {
    expect(composeSegment({ mode: 'values', values: [30, 1, 15] })).toBe('1,15,30')
    expect(composeSegment({ mode: 'values', values: [] })).toBe('*')
  })
})

describe('setSegment 日/周互斥', () => {
  const base = ['0', '0', '0', '*', '*', '?']
  it('日受限 → 周自动落 ?', () => {
    const segs = ['0', '0', '0', '*', '*', '1-5']
    expect(setSegment(segs, 3, 'L')).toEqual(['0', '0', '0', 'L', '*', '?'])
  })
  it('周受限 → 日自动落 ?', () => {
    expect(setSegment(base, 5, '2#3')).toEqual(['0', '0', '0', '?', '*', '2#3'])
  })
  it('不受限的写入不动对侧;入参不被改', () => {
    const segs = ['0', '0', '0', '15', '*', '?']
    const next = setSegment(segs, 1, '30')
    expect(next).toEqual(['0', '30', '0', '15', '*', '?'])
    expect(segs[1]).toBe('0')
  })
  it('isRestricted:* 与 ? 都不算受限', () => {
    expect(isRestricted('*')).toBe(false)
    expect(isRestricted('?')).toBe(false)
    expect(isRestricted('L')).toBe(true)
  })
})
