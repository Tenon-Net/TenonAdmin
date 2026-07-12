# Chart(ECharts 封装)

封 [`vue-echarts`](https://github.com/ecomfe/vue-echarts) + ECharts,**自动跟随应用明暗主题 / accent 换色**、自带 `autoresize`。
一个目录一族:`index.vue`(BaseChart 基座)+ `LineChart` / `BarChart` / `PieChart`(薄预设)。

## 什么时候用哪个

- **预设**(`LineChart` / `BarChart` / `PieChart`):常规折线 / 柱 / 饼,传 `data`,少写模板。
- **BaseChart**(`Chart/index.vue`):其余一切(仪表盘 / 雷达 / 散点 / 组合图 / 大屏),自己拼 `option` 传 `:option`。

预设内部就是拼好 `option` 再套 BaseChart —— 主题联动只在 BaseChart 一处。

## 主题联动(零配置)

BaseChart 用 `computed` 依赖 `app.isDark` / `app.accent`,变化时**现读 `styles/tokens.css` 的 CSS 变量**
(`--color-primary/success/warning/danger/info`、`--color-text-*`、`--color-border`)拼 echarts 主题对象。
所以切明暗 / 换 accent,图表色板、文字、轴线即时跟随,**无需在页面传任何主题参数**。背景恒透明(吃卡片底色)。

## 按需注册(控包体)

图种/组件在 `echarts.ts` 里 `use([...])` 注册,**不引 echarts 全量**。已注册:
`Canvas` 渲染器 + `Line/Bar/Pie/Gauge/Radar/Scatter` + `Title/Tooltip/Legend/Grid/DataZoom/Toolbox`。
要用地图 / 关系图 / 3D 等,在 `echarts.ts` 加一行 `use([...])` 即可。

## 用法

```vue
<script setup lang="ts">
import Chart from '@/components/Chart/index.vue'
import LineChart from '@/components/Chart/LineChart.vue'
import BarChart from '@/components/Chart/BarChart.vue'
import PieChart from '@/components/Chart/PieChart.vue'
</script>

<template>
  <!-- 折线:多 series 自动出图例 -->
  <LineChart
    :categories="['一', '二', '三', '四', '五']"
    :series="[{ name: '登录', data: [120, 132, 101, 134, 90] }]"
    :title="'近 5 日趋势'"
  />

  <!-- 饼/环 -->
  <PieChart :data="[{ name: '角色', value: 24 }, { name: '用户', value: 186 }]" />

  <!-- 高级:直接传 option(仪表盘 / 大屏等) -->
  <Chart :option="gaugeOption" style="height: 260px" />
</template>
```

## Props

**BaseChart**:`option`(ECharts option 对象,必填)、`loading?`、`autoresize?=true`。其余属性 `$attrs` 透传 `<v-chart>`;
高度默认 `320px`,用 `style="height: …"` 或 class 覆盖。

**LineChart** / **BarChart**:`categories: string[]`、`series: { name; data: number[] }[]`、`title?`;LineChart 另有 `smooth?=true`。
**PieChart**:`data: { name; value: number }[]`、`title?`、`ring?=true`(false 为实心饼)。
