<script setup lang="ts">
// 饼/环预设:传 data(name+value)即出图;复杂需求直接用 BaseChart 传 :option。
import { computed } from 'vue'
import type { EChartsOption } from 'echarts'
import Chart from './index.vue'

defineOptions({ inheritAttrs: false })
const props = withDefaults(
  defineProps<{
    data: { name: string; value: number }[]
    title?: string
    ring?: boolean
  }>(),
  { title: '', ring: true },
)

const option = computed<EChartsOption>(() => ({
  title: props.title ? { text: props.title } : undefined,
  tooltip: { trigger: 'item' },
  legend: { bottom: 0 },
  series: [
    {
      type: 'pie',
      radius: props.ring ? ['42%', '68%'] : '68%',
      center: ['50%', '46%'],
      data: props.data,
    },
  ],
}))
</script>

<template>
  <Chart :option="option" v-bind="$attrs" />
</template>
