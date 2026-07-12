<script setup lang="ts">
// BaseChart:封 vue-echarts,自动跟随应用明暗主题 / accent。传 :option 即用。
// 图种按需注册在 ./echarts(引入该模块即触发注册)。默认高 320px,可用 style/class 覆盖。
import { computed } from 'vue'
import VChart from 'vue-echarts'
import type { EChartsOption } from 'echarts'
import { buildEChartsTheme } from './echarts'
import { useAppStore } from '@/stores/app'

defineOptions({ inheritAttrs: false })
const props = withDefaults(
  defineProps<{ option: EChartsOption; loading?: boolean; autoresize?: boolean }>(),
  { loading: false, autoresize: true },
)
const app = useAppStore()

// 显式读 isDark / accent 作依赖:切主题或换 accent 时重算 → 现读 tokens 得新色板。
const theme = computed(() => {
  void app.isDark
  void app.accent
  return buildEChartsTheme()
})
</script>

<template>
  <VChart
    class="chart"
    :option="props.option"
    :theme="theme"
    :loading="props.loading"
    :autoresize="props.autoresize"
    v-bind="$attrs"
  />
</template>

<style scoped>
.chart {
  width: 100%;
  height: 320px;
}
</style>
