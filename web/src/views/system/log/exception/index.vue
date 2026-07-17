<script setup lang="ts">
// 异常日志 = 只读 ProTable + 详情抽屉。后端无 exception/{id},分页项已含全字段 → 抽屉直接用行数据。
// 只记未捕获异常(程序缺陷);业务异常不进本表。堆栈保持危险色 <pre>——堆栈非代码,高亮无意义。
import { h, ref } from 'vue'
import {
  NButton, NDrawer, NDrawerContent, NDescriptions, NDescriptionsItem, useMessage,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import { useConfirm } from '@/composables/useConfirm'
import { logApi } from '@/api'
import { translateError } from '@/utils/error'
import type { SysExceptionLog } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { confirm } = useConfirm()
const tableRef = ref<ProTableInst<SysExceptionLog>>()

const columns: ProTableColumn<SysExceptionLog>[] = [
  { key: 'exceptionType', title: () => t('log.exceptionType'), ellipsis: { tooltip: true }, search: true },
  { key: 'message', title: () => t('log.exceptionMessage'), ellipsis: { tooltip: true }, render: (r) => r.message || '—' },
  { key: 'httpMethod', title: () => t('log.method'), width: 90 },
  { key: 'path', title: () => t('log.path'), ellipsis: { tooltip: true }, search: true },
  { key: 'operator', title: () => t('log.operator'), width: 120, render: (r) => operatorText(r) },
  { key: 'ip', title: () => t('log.ip'), render: (r) => r.ip || '—' },
  { key: 'createTime', title: () => t('common.createTime'), format: 'datetime', search: { type: 'daterange' } },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 90,
    hideInSetting: true,
    render: (r) =>
      h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openDetail(r) }, () => t('log.detail')),
  },
]

// ── 详情抽屉(只读,行数据直填)──
const showDetail = ref(false)
const detailRow = ref<SysExceptionLog | null>(null)
function openDetail(r: SysExceptionLog) {
  detailRow.value = r
  showDetail.value = true
}
/** 触发人展示:优先姓名,姓名缺失(用户已删/匿名端点)回落原始 Id,再无则占位。 */
function operatorText(r: SysExceptionLog) {
  return r.operatorName || (r.operatorId != null ? String(r.operatorId) : '—')
}

function clearLogs() {
  confirm({
    type: 'error',
    content: t('log.clearExceptionConfirm'),
    action: () => logApi.exceptionClear(),
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
    :fetcher="logApi.exceptionPage"
    storage-key="sys-log-exception"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-button v-auth="'DELETE:/api/v1/sys/log/exception'" type="error" secondary @click="clearLogs">
        <template #icon><AppIcon icon="ph:trash" :size="16" /></template>{{ t('log.clear') }}
      </n-button>
    </template>
  </ProTable>

  <n-drawer v-model:show="showDetail" :width="620" placement="right">
    <n-drawer-content :title="t('log.detail')" closable>
      <n-descriptions v-if="detailRow" label-placement="left" :column="1" bordered size="small">
        <n-descriptions-item :label="t('log.exceptionType')">{{ detailRow.exceptionType }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.method')">{{ detailRow.httpMethod }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.path')">{{ detailRow.path }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.traceId')">{{ detailRow.traceId || '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.operator')">{{ operatorText(detailRow) }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.ip')">{{ detailRow.ip || '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.userAgent')">{{ detailRow.userAgent || '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('common.createTime')">{{ detailRow.createTime }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.exceptionMessage')">
          <pre class="exception">{{ detailRow.message || '—' }}</pre>
        </n-descriptions-item>
        <n-descriptions-item :label="t('log.stackTrace')">
          <pre class="exception">{{ detailRow.stackTrace || '—' }}</pre>
        </n-descriptions-item>
      </n-descriptions>
    </n-drawer-content>
  </n-drawer>
</template>

<style scoped>
.exception {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-all;
  font-family: var(--font-mono, ui-monospace, monospace);
  font-size: 12px;
  line-height: 1.5;
  color: var(--color-danger);
}
</style>
