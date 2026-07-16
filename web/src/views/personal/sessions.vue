<script setup lang="ts">
// 我的会话(个人视角的"登录设备"):[ActiveSession] 人人可用,静态路由不进菜单(理由同 notice)。
// 数据量受会话并发上限约束(个位数)→ 不用 ProTable,onMounted 拉一次 + n-data-table 静态渲染。
// 踢当前设备置灰:那等于自杀,请走"退出登录";管理端全量视角在 system/session。
import { h, onMounted, ref } from 'vue'
import { NCard, NDataTable, NButton, NTag, useMessage, type DataTableColumns } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { personalApi } from '@/api'
import type { MySessionItem } from '@/types/api'
import { uaSummary } from '@/utils/ua'
import { useConfirm } from '@/composables/useConfirm'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()
const { confirm } = useConfirm()

const loading = ref(true)
const rows = ref<MySessionItem[]>([])
const rowKey = (r: MySessionItem) => r.sessionId

async function load() {
  loading.value = true
  try {
    rows.value = await personalApi.sessions()
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}
onMounted(load)

const fmt = (s?: string | null) => (s ?? '').replace('T', ' ').slice(0, 19)

function kick(r: MySessionItem) {
  confirm({
    type: 'warning',
    content: t('session.kickMineConfirm'),
    action: () => personalApi.kickSession(r.sessionId),
    successMsg: t('session.kicked'),
  }).then((ok) => {
    if (ok) load()
  })
}

const columns: DataTableColumns<MySessionItem> = [
  {
    key: 'device',
    title: () => t('session.device'),
    render: (r) =>
      h('div', { class: 'device-cell' }, [
        h('span', uaSummary(r.userAgent)),
        r.isCurrent ? h(NTag, { type: 'success', size: 'small', bordered: false }, () => t('session.current')) : null,
      ]),
  },
  { key: 'ip', title: () => t('session.ip'), width: 140, render: (r) => r.ip || '—' },
  { key: 'loginTime', title: () => t('session.loginTime'), width: 180, render: (r) => fmt(r.loginTime) },
  { key: 'expiresAt', title: () => t('session.expiresAt'), width: 180, render: (r) => fmt(r.expiresAt) },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 110,
    render: (r) =>
      h(
        NButton,
        { size: 'small', quaternary: true, type: 'error', disabled: r.isCurrent, onClick: () => kick(r) },
        () => t('session.kick'),
      ),
  },
]
</script>

<template>
  <n-card :bordered="true" :title="t('session.mineTitle')" style="max-width: 960px">
    <n-data-table :columns="columns" :data="rows" :loading="loading" :row-key="rowKey" :pagination="false" />
  </n-card>
</template>

<style scoped>
:deep(.device-cell) {
  display: flex;
  align-items: center;
  gap: 8px;
}
</style>
