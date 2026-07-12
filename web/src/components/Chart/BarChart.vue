<script setup lang="ts">
// 柱状预设:传 categories + series 即出图;复杂需求直接用 BaseChart 传 :option。
import { computed } from 'vue'
import type { EChartsOption } from 'echarts'
import Chart from './index.vue'

defineOptions({ inheritAttrs: false })
const props = withDefaults(
  defineProps<{
    categories: string[]
    series: { name: string; data: number[] }[]
    title?: string
  }>(),
  { title: '' },
)

const option = computed<EChartsOption>(() => ({
  title: props.title ? { text: props.title } : undefined,
  tooltip: { trigger: 'axis' },
  legend: props.series.length > 1 ? {} : undefined,
  grid: { left: 8, right: 16, bottom: 8, top: props.title ? 48 : 24, containLabel: true },
  xAxis: { type: 'category', data: props.categories },
  yAxis: { type: 'value' },
  series: props.series.map((s) => ({ name: s.name, type: 'bar', data: s.data })),
}))
</script>

<template>
  <Chart :option="option" v-bind="$attrs" />
</template>
