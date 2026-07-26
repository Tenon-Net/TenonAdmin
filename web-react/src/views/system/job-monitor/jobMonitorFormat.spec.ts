import { describe, expect, it } from 'vitest'
import { buildJobTrendOption, heartbeatAge } from './jobMonitorFormat'

describe('buildJobTrendOption', () => {
  it('柱线组合:成功走柱、失败走线,名字/数据各归各,legend 恒出', () => {
    const opt = buildJobTrendOption(['07-01', '07-02'], [3, 5], [1, 0], { success: '成功', failed: '失败' })
    const series = opt.series as Array<{ name: string; type: string; data: number[] }>
    expect(series).toHaveLength(2)
    expect(series[0]).toMatchObject({ name: '成功', type: 'bar', data: [3, 5] })
    expect(series[1]).toMatchObject({ name: '失败', type: 'line', data: [1, 0] })
    expect((opt.xAxis as { data: string[] }).data).toEqual(['07-01', '07-02'])
    expect(opt.legend).toBeTruthy()
  })
  it('y 轴 minInterval=1(次数是整数,防出 0.5 刻度)', () => {
    const opt = buildJobTrendOption([], [], [], { success: 's', failed: 'f' })
    expect((opt.yAxis as { minInterval: number }).minInterval).toBe(1)
  })
})

describe('heartbeatAge', () => {
  const now = new Date('2026-07-27T10:00:00').getTime()
  it('整秒向下取整', () => {
    expect(heartbeatAge('2026-07-27T09:59:58.400', now)).toBe(1)
    expect(heartbeatAge('2026-07-27T09:59:00', now)).toBe(60)
  })
  it('未来时刻(时钟漂移)不给负数;坏时刻给 0', () => {
    expect(heartbeatAge('2026-07-27T10:00:05', now)).toBe(0)
    expect(heartbeatAge('not-a-date', now)).toBe(0)
  })
})
