<script setup lang="ts">
// 任务监控(G7)= 4 张 stat 卡 + 近 14 日成败趋势 + 即将执行 + 集群节点,整页吃一个 dashboard 端点。
// 15 秒轮询:页面被 keep-alive,切走的标签没必要继续打点 → onDeactivated 也要停,回来再启;
// onUnmounted 兜底清定时器,防组件销毁后计时器泄漏还在发请求。
import { computed, h, onActivated, onDeactivated, onMounted, onUnmounted, ref } from 'vue'
import { NGrid, NGi, NCard, NSpin, NTag, NDataTable, useMessage, type DataTableColumns } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import BaseChart from '@/components/Chart/index.vue'
import type { EChartsOption } from 'echarts'
import { jobApi } from '@/api'
import { translateError } from '@/utils/error'
import type { JobDashboard, JobNodeItem, JobUpcomingItem } from '@/types/api'

const { t } = useI18n()
const message = useMessage()

const data = ref<JobDashboard | null>(null)
const loading = ref(true)

async function load(silent = true) {
  try {
    data.value = await jobApi.dashboard()
  } catch (e) {
    // 首次失败要让用户知道;轮询失败静默(网络抖一下不必每 15 秒糊一脸红)
    if (!silent) message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

let timer: number | null = null
function start() {
  if (timer != null) return
  timer = window.setInterval(() => void load(), 15_000)
}
function stop() {
  if (timer != null) {
    window.clearInterval(timer)
    timer = null
  }
}
onMounted(() => {
  void load(false)
  start()
})
onActivated(start)
onDeactivated(stop)
onUnmounted(stop)

/** 计数未到位时显示 —,而不是先闪个 0。 */
const num = (v: number | undefined) => (v === undefined ? '—' : v)
const stats = computed(() => [
  { key: 'todaySuccess', icon: 'ph:check-circle-duotone', value: num(data.value?.todaySuccess), color: 'var(--color-success)' },
  { key: 'todayFailed', icon: 'ph:x-circle-duotone', value: num(data.value?.todayFailed), color: 'var(--color-danger)' },
  { key: 'running', icon: 'ph:circle-notch-duotone', value: num(data.value?.running), color: 'var(--color-info)' },
  { key: 'totalJobs', icon: 'ph:clock-countdown-duotone', value: num(data.value?.totalJobs), color: 'var(--color-primary)' },
])

// 柱线组合:成功是当日总量,用柱;失败通常小一个量级,压在柱上会看不见,用线单独描出来。
const trendOption = computed<EChartsOption>(() => {
  const trend = data.value?.trend ?? []
  return {
    tooltip: { trigger: 'axis' },
    // 图例位置写死在顶部:留给默认值时它会落到绘图区底部、压在 x 轴日期标签上(实测)。
    // grid.top 的 32 就是给它留的位置。
    legend: { top: 0 },
    grid: { left: 8, right: 16, bottom: 8, top: 32, containLabel: true },
    xAxis: { type: 'category', data: trend.map((p) => p.date.slice(5)) }, // MM-dd 足够,14 天跨年概率忽略
    yAxis: { type: 'value', minInterval: 1 },
    series: [
      { name: t('job.monitor.trendSuccess'), type: 'bar', data: trend.map((p) => p.success) },
      { name: t('job.monitor.trendFailed'), type: 'line', smooth: true, data: trend.map((p) => p.failed) },
    ],
  }
})

const fmt = (s?: string | null) => (s ? s.slice(0, 19).replace('T', ' ') : '—')

/** 最后心跳的相对时间(轮询 15 秒一刷,精确到秒/分/时够用)。 */
function relative(iso: string): string {
  const diff = Math.max(0, Date.now() - new Date(iso).getTime())
  const sec = Math.floor(diff / 1000)
  if (sec < 10) return t('job.monitor.justNow')
  if (sec < 60) return t('job.monitor.secondsAgo', { n: sec })
  if (sec < 3600) return t('job.monitor.minutesAgo', { n: Math.floor(sec / 60) })
  return t('job.monitor.hoursAgo', { n: Math.floor(sec / 3600) })
}

const upcomingColumns: DataTableColumns<JobUpcomingItem> = [
  { title: () => t('job.monitor.upcomingJob'), key: 'name', ellipsis: { tooltip: true } },
  { title: () => t('job.monitor.upcomingTime'), key: 'nextRunTime', width: 170, render: (r) => h('span', { class: 'tabular' }, fmt(r.nextRunTime)) },
]

const nodeColumns: DataTableColumns<JobNodeItem> = [
  { title: () => t('job.monitor.nodeName'), key: 'nodeName', ellipsis: { tooltip: true } },
  {
    title: () => t('job.monitor.role'),
    key: 'isLeader',
    width: 90,
    render: (r) =>
      h(NTag, { size: 'small', bordered: false, type: r.isLeader ? 'primary' : 'default' }, () =>
        t(r.isLeader ? 'job.monitor.leader' : 'job.monitor.standby'),
      ),
  },
  { title: () => t('job.monitor.lastHeartbeat'), key: 'lastHeartbeat', width: 110, render: (r) => relative(r.lastHeartbeat) },
  { title: () => t('job.monitor.workerId'), key: 'workerId', width: 90, align: 'center' },
  { title: () => t('job.monitor.pid'), key: 'pid', width: 90, align: 'center' },
]
</script>

<template>
  <div class="view">
    <n-grid :cols="'1 s:2 l:4'" responsive="screen" :x-gap="16" :y-gap="16">
      <n-gi v-for="s in stats" :key="s.key">
        <n-card :bordered="true">
          <div class="stat">
            <div class="stat-ico" :style="{ color: s.color }"><Icon :icon="s.icon" :width="28" /></div>
            <div>
              <div class="stat-val tabular">{{ s.value }}</div>
              <div class="stat-label">{{ t(`job.monitor.${s.key}`) }}</div>
            </div>
          </div>
        </n-card>
      </n-gi>
    </n-grid>

    <n-card :title="t('job.monitor.trendTitle')" :bordered="true">
      <n-spin :show="loading">
        <BaseChart :option="trendOption" />
      </n-spin>
    </n-card>

    <n-grid :cols="'1 l:2'" responsive="screen" :x-gap="16" :y-gap="16">
      <n-gi>
        <n-card :title="t('job.monitor.upcomingTitle')" :bordered="true">
          <n-data-table
            size="small"
            :columns="upcomingColumns"
            :data="data?.upcoming ?? []"
            :loading="loading"
            :row-key="(r: JobUpcomingItem) => `${r.jobId}-${r.nextRunTime}`"
            :pagination="false"
          />
        </n-card>
      </n-gi>
      <n-gi>
        <n-card :title="t('job.monitor.nodesTitle')" :bordered="true">
          <n-data-table
            size="small"
            :columns="nodeColumns"
            :data="data?.nodes ?? []"
            :loading="loading"
            :row-key="(r: JobNodeItem) => r.nodeName"
            :pagination="false"
          />
        </n-card>
      </n-gi>
    </n-grid>
  </div>
</template>

<style scoped>
.view {
  display: flex;
  flex-direction: column;
  gap: var(--gap-card);
}
.stat {
  display: flex;
  align-items: center;
  gap: 16px;
}
/* 图标底盘:取语义色 12% 作浅底,同工作台手法,避免图标"裸浮" */
.stat-ico {
  flex-shrink: 0;
  width: 48px;
  height: 48px;
  border-radius: var(--radius-md);
  display: flex;
  align-items: center;
  justify-content: center;
  background: color-mix(in srgb, currentColor 12%, transparent);
}
.stat-val {
  font-size: 28px;
  font-weight: 700;
  color: var(--color-text-primary);
  line-height: 1.1;
}
.stat-label {
  color: var(--color-text-secondary);
  margin-top: 2px;
}
</style>
