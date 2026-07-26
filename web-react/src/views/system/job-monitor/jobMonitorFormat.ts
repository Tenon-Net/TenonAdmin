// 任务监控页纯逻辑(变异钉):趋势图 option 构造 + 心跳相对时长。页面只做接线与展示。
import type { EChartsOption } from 'echarts'

/**
 * 近 14 日成败趋势:柱线组合,legend 恒出(双序列)。
 * 成功是当日总量,用柱;失败通常小一个量级,压在柱上会看不见,用线单独描出来。
 */
export function buildJobTrendOption(
  categories: string[],
  success: number[],
  failed: number[],
  names: { success: string; failed: string },
): EChartsOption {
  return {
    tooltip: { trigger: 'axis' },
    legend: {},
    grid: { left: 8, right: 16, bottom: 8, top: 24, containLabel: true },
    xAxis: { type: 'category', data: categories },
    yAxis: { type: 'value', minInterval: 1 },
    series: [
      { name: names.success, type: 'bar', data: success },
      { name: names.failed, type: 'line', smooth: true, data: failed },
    ],
  }
}

/** 心跳距今秒数(向下取整,不小于 0;坏时刻给 0,别让 NaN 进文案)。 */
export function heartbeatAge(iso: string, nowMs: number): number {
  const ts = new Date(iso).getTime()
  if (Number.isNaN(ts)) return 0
  return Math.max(0, Math.floor((nowMs - ts) / 1000))
}
