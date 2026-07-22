// dashboard 纯逻辑(变异钉):echarts option 构造 + 计数兜底 + 菜单可见叶子收集 + 时间截断。
// 对齐 Vue 侧 Chart/LineChart.vue、Chart/PieChart.vue、workbench.vue、biz.vue 的同名逻辑。
// echarts 图种(line/pie)在 @/lib/echarts 注册,<Chart> 挂载即触发;此处只产 option。
import type { EChartsOption } from 'echarts'
import type { MenuNode } from '@/types/menu'

/** 折线 option:categories + 多序列 → 平滑折线;>1 序列才出图例。对齐 Vue LineChart 预设。 */
export function buildLineOption(categories: string[], series: { name: string; data: number[] }[]): EChartsOption {
  return {
    tooltip: { trigger: 'axis' },
    legend: series.length > 1 ? {} : undefined,
    grid: { left: 8, right: 16, bottom: 8, top: 24, containLabel: true },
    xAxis: { type: 'category', boundaryGap: false, data: categories },
    yAxis: { type: 'value' },
    series: series.map((s) => ({ name: s.name, type: 'line', smooth: true, data: s.data })),
  }
}

/** 环形饼 option:name+value 数据。对齐 Vue PieChart 预设(ring 默认)。 */
export function buildPieOption(data: { name: string; value: number }[]): EChartsOption {
  return {
    tooltip: { trigger: 'item' },
    legend: { bottom: 0 },
    series: [{ type: 'pie', radius: ['42%', '68%'], center: ['50%', '46%'], data }],
  }
}

/** 计数未到位显示 —,而非先闪 0 再跳真值。 */
export function num(v: number | undefined): number | string {
  return v === undefined ? '—' : v
}

/** 菜单树的可见叶子(带 path)= 快捷入口候选;excludePath 排除当前页自身。递归下钻,非可见整枝跳过。 */
export function visibleLeaves(tree: MenuNode[], excludePath?: string): MenuNode[] {
  const out: MenuNode[] = []
  for (const n of tree) {
    if (!n.visible) continue
    if (n.children?.length) out.push(...visibleLeaves(n.children, excludePath))
    else if (n.path && n.path !== excludePath) out.push(n)
  }
  return out
}

/** ISO 时间截到 分(去 T)。 */
export function fmtTime(s?: string | null): string {
  return (s ?? '').replace('T', ' ').slice(0, 16)
}
