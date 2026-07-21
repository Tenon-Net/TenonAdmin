import { useEffect, useRef, type CSSProperties } from 'react'
import { init } from 'echarts/core'
import type { EChartsOption } from 'echarts'
import { useAppStore, isDark } from '@/stores/app'
import { buildEChartsTheme } from '@/lib/echarts'

export interface ChartProps {
  option: EChartsOption
  height?: number | string
  loading?: boolean
  className?: string
  style?: CSSProperties
}

/**
 * 基础图表:**裸 echarts.init**(不引 vue-echarts 那类 wrapper 依赖),跟随应用明暗/accent 重建主题。
 * 图种按需注册在 `@/lib/echarts`(import 该模块即触发 `use([...])`)。对应 Vue 侧 `Chart/index.vue`。
 * 命令式渲染(canvas)在 happy-dom 无法单测,判据落 `buildEChartsTheme`(纯逻辑)+ dev/B12 实点。
 */
export function Chart({ option, height = 320, loading, className, style }: ChartProps) {
  const elRef = useRef<HTMLDivElement>(null)
  const chartRef = useRef<ReturnType<typeof init> | null>(null)
  const dark = useAppStore(isDark)
  const accent = useAppStore((s) => s.accent)

  // option 存 ref:主题重建 effect 要读最新 option 种入,但不该把 option 变化混进"重建"依赖。
  const optionRef = useRef(option)
  optionRef.current = option

  // 主题(明暗/accent)变 → dispose 旧图、以新主题 init(echarts 主题在 init 时固化),并种入当前 option。
  useEffect(() => {
    const el = elRef.current
    if (!el) return
    const chart = init(el, buildEChartsTheme())
    chartRef.current = chart
    chart.setOption(optionRef.current)
    const onResize = () => chart.resize()
    window.addEventListener('resize', onResize)
    return () => {
      window.removeEventListener('resize', onResize)
      chart.dispose()
      chartRef.current = null
    }
  }, [dark, accent])

  // option 变 → setOption(notMerge:true 清掉旧系列残留)。
  useEffect(() => {
    chartRef.current?.setOption(option, true)
  }, [option])

  // loading 态。
  useEffect(() => {
    const c = chartRef.current
    if (!c) return
    if (loading) c.showLoading()
    else c.hideLoading()
  }, [loading])

  return <div ref={elRef} className={className} style={{ width: '100%', height, ...style }} />
}
