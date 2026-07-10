<script setup lang="ts">
// 登录日志 = 只读 ProTable:列驱动搜索(账号/结果)+ 分页;success 列显式红/绿 typeMap 保证「失败=红」。
// 工具栏一枚「清空」(硬删,不可恢复)走 useConfirm 二次确认(对齐 simpleadmin 日志页)。
import { ref } from 'vue'
import { NButton, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useProTableLabels } from '@/composables/useProTableLabels'
import { logApi } from '@/api'
import { translateError } from '@/utils/error'
import type { SysLoginLog } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { confirm } = useConfirm()
const labels = useProTableLabels()
const tableRef = ref<ProTableInst<SysLoginLog>>()

const columns: ProTableColumn<SysLoginLog>[] = [
  { key: 'account', title: () => t('log.account'), search: true },
  {
    key: 'success',
    title: () => t('log.result'),
    tag: true,
    search: true,
    options: [
      { label: () => t('log.success'), value: true, tagType: 'success' },
      { label: () => t('log.failed'), value: false, tagType: 'error' },
    ],
  },
  { key: 'resultCode', title: () => t('log.resultCode'), width: 100 },
  { key: 'userId', title: () => t('log.userId'), render: (r) => r.userId ?? '—' },
  { key: 'ip', title: () => t('log.ip'), render: (r) => r.ip || '—' },
  { key: 'userAgent', title: () => t('log.userAgent'), ellipsis: { tooltip: true }, render: (r) => r.userAgent || '—' },
  { key: 'createTime', title: () => t('common.createTime'), format: 'datetime' },
]

function clearLogs() {
  confirm({
    type: 'error',
    content: t('log.clearLoginConfirm'),
    action: () => logApi.loginClear(),
    successMsg: t('log.cleared'),
  }).then((ok) => {
    if (ok) tableRef.value?.refresh()
  })
}
</script>

<template>
  <ProTable
    ref="tableRef"
    :columns="columns"
    :fetcher="logApi.loginPage"
    :title="t('log.loginTitle')"
    :labels="labels"
    storage-key="sys-log-login"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-button v-auth="'DELETE:/api/v1/sys/log/login'" type="error" secondary @click="clearLogs">
        <template #icon><AppIcon icon="ph:trash" :size="16" /></template>{{ t('log.clear') }}
      </n-button>
    </template>
  </ProTable>
</template>
