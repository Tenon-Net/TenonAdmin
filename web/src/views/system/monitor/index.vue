<script setup lang="ts">
// 服务器监控 = 卡片 + 手动刷新(不轮询:CPU 快照后端每次采样 500ms,自动轮询会持续占用)。
import { onMounted, ref } from 'vue'
import { NGrid, NGi, NCard, NProgress, NButton, NSpin, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import { monitorApi } from '@/api'
import { translateError } from '@/utils/error'
import type { ServerInfoOutput } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const info = ref<ServerInfoOutput | null>(null)
const loading = ref(false)

async function load() {
  loading.value = true
  try {
    info.value = await monitorApi.server()
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}
onMounted(load)

/** 字节 → 人类可读(二进制单位)。 */
function fmtBytes(n: number): string {
  if (n <= 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.min(Math.floor(Math.log(n) / Math.log(1024)), units.length - 1)
  return `${(n / 1024 ** i).toFixed(i === 0 ? 0 : 2)} ${units[i]}`
}
/** 运行秒数 → d/h/m。 */
function fmtUptime(s: number): string {
  const d = Math.floor(s / 86400)
  const h = Math.floor((s % 86400) / 3600)
  const m = Math.floor((s % 3600) / 60)
  return [d ? `${d}${t('monitor.days')}` : '', h ? `${h}${t('monitor.hours')}` : '', `${m}${t('monitor.minutes')}`]
    .filter(Boolean).join(' ')
}
/** 内存/磁盘占用百分比(整数,0–100)。 */
function pct(used: number, total: number): number {
  return total > 0 ? Math.round((used / total) * 100) : 0
}
function usageStatus(p: number): 'success' | 'warning' | 'error' {
  return p >= 90 ? 'error' : p >= 75 ? 'warning' : 'success'
}
</script>

<template>
  <div class="monitor">
    <div class="bar">
      <n-button size="small" secondary :loading="loading" @click="load">
        <template #icon><AppIcon icon="ph:arrow-clockwise" :size="16" /></template>{{ t('monitor.refresh') }}
      </n-button>
    </div>

    <n-spin :show="loading">
      <n-grid v-if="info" cols="1 s:2 l:4" responsive="screen" :x-gap="12" :y-gap="12">
        <!-- CPU -->
        <n-gi>
          <n-card :title="t('monitor.cpu')" size="small">
            <div class="metric">{{ info.processCpuPercent.toFixed(1) }}<span class="unit">%</span></div>
            <n-progress
              type="line" :percentage="Math.round(info.processCpuPercent)"
              :status="usageStatus(Math.round(info.processCpuPercent))" :show-indicator="false" :height="6"
            />
            <div class="sub">{{ t('monitor.processorCount') }}: {{ info.processorCount }} · {{ t('monitor.threadCount') }}: {{ info.threadCount }}</div>
          </n-card>
        </n-gi>

        <!-- 内存 -->
        <n-gi>
          <n-card :title="t('monitor.memory')" size="small">
            <div class="metric">{{ fmtBytes(info.processWorkingSetBytes) }}</div>
            <n-progress
              type="line" :percentage="pct(info.processWorkingSetBytes, info.totalAvailableMemoryBytes)"
              :status="usageStatus(pct(info.processWorkingSetBytes, info.totalAvailableMemoryBytes))" :height="6"
            />
            <div class="sub">{{ t('monitor.gcHeap') }}: {{ fmtBytes(info.gcHeapBytes) }} · {{ t('monitor.totalMemory') }}: {{ fmtBytes(info.totalAvailableMemoryBytes) }}</div>
          </n-card>
        </n-gi>

        <!-- 运行时 -->
        <n-gi span="1 s:2">
          <n-card :title="t('monitor.runtime')" size="small">
            <div class="rows">
              <div><span class="k">{{ t('monitor.machineName') }}</span><span class="v">{{ info.machineName }}</span></div>
              <div><span class="k">{{ t('monitor.framework') }}</span><span class="v">{{ info.frameworkDescription }}</span></div>
              <div><span class="k">{{ t('monitor.os') }}</span><span class="v">{{ info.osDescription }}</span></div>
              <div><span class="k">{{ t('monitor.arch') }}</span><span class="v">{{ info.processArchitecture }}</span></div>
              <div><span class="k">{{ t('monitor.uptime') }}</span><span class="v">{{ fmtUptime(info.processUptimeSeconds) }}</span></div>
            </div>
          </n-card>
        </n-gi>

        <!-- 磁盘 -->
        <n-gi v-for="disk in info.disks" :key="disk.name" span="1 s:2 l:1">
          <n-card :title="`${t('monitor.disk')} ${disk.name}`" size="small">
            <div class="metric small">{{ fmtBytes(disk.totalBytes - disk.freeBytes) }} / {{ fmtBytes(disk.totalBytes) }}</div>
            <n-progress
              type="line" :percentage="pct(disk.totalBytes - disk.freeBytes, disk.totalBytes)"
              :status="usageStatus(pct(disk.totalBytes - disk.freeBytes, disk.totalBytes))" :height="6"
            />
            <div class="sub">{{ t('monitor.free') }}: {{ fmtBytes(disk.freeBytes) }}</div>
          </n-card>
        </n-gi>
      </n-grid>
    </n-spin>
  </div>
</template>

<style scoped>
.monitor {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.bar {
  display: flex;
  justify-content: flex-end;
}
.metric {
  font-size: 28px;
  font-weight: 600;
  line-height: 1.2;
  margin-bottom: 10px;
}
.metric.small {
  font-size: 18px;
}
.unit {
  font-size: 16px;
  margin-left: 2px;
  color: var(--color-text-secondary, #999);
}
.sub {
  margin-top: 8px;
  font-size: 12px;
  color: var(--color-text-secondary, #999);
}
.rows {
  display: grid;
  gap: 8px;
  font-size: 13px;
}
.rows .k {
  display: inline-block;
  width: 88px;
  color: var(--color-text-secondary, #999);
}
.rows .v {
  word-break: break-all;
}
</style>
