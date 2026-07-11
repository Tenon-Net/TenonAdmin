<script setup lang="ts">
// 操作日志 = 只读 ProTable(操作名/结果可搜)+ 详情抽屉。后端无 op/{id},分页项已含全字段 → 抽屉直接用行数据。
// paramJson 走 <pre> 兜底美化(JSON.parse→stringify;失败原样);仅此一处 JSON 展示,不落地 CodeBlock(COMPONENTS.md 约定)。
import { h, ref } from 'vue'
import {
  NButton, NTag, NDrawer, NDrawerContent, NDescriptions, NDescriptionsItem, useMessage,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import AppIcon from '@/components/AppIcon.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useProTableLabels } from '@/composables/useProTableLabels'
import { logApi } from '@/api'
import { translateError } from '@/utils/error'
import type { SysOpLog } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { confirm } = useConfirm()
const labels = useProTableLabels()
const tableRef = ref<ProTableInst<SysOpLog>>()

const columns: ProTableColumn<SysOpLog>[] = [
  { key: 'title', title: () => t('log.opName'), search: true },
  { key: 'httpMethod', title: () => t('log.method'), width: 90 },
  { key: 'path', title: () => t('log.path'), ellipsis: { tooltip: true } },
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
  { key: 'operator', title: () => t('log.operator'), width: 120, render: (r) => operatorText(r) },
  { key: 'elapsedMs', title: () => t('log.elapsed'), width: 100, render: (r) => `${r.elapsedMs} ms` },
  { key: 'ip', title: () => t('log.ip'), render: (r) => r.ip || '—' },
  { key: 'createTime', title: () => t('common.createTime'), format: 'datetime' },
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
const detailRow = ref<SysOpLog | null>(null)
function openDetail(r: SysOpLog) {
  detailRow.value = r
  showDetail.value = true
}
/** 操作人展示:优先姓名,姓名缺失(用户已删/未解析)回落原始 Id,再无则占位。 */
function operatorText(r: SysOpLog) {
  return r.operatorName || (r.operatorId != null ? String(r.operatorId) : '—')
}
/** paramJson 美化:parse→stringify(2);非法 JSON 原样返回;空值占位。 */
function prettyParam(json?: string | null) {
  if (!json) return '—'
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    return json
  }
}

function clearLogs() {
  confirm({
    type: 'error',
    content: t('log.clearOpConfirm'),
    action: () => logApi.opClear(),
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
    :fetcher="logApi.opPage"
    :title="t('log.opTitle')"
    :labels="labels"
    storage-key="sys-log-op"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-button v-auth="'DELETE:/api/v1/sys/log/op'" type="error" secondary @click="clearLogs">
        <template #icon><AppIcon icon="ph:trash" :size="16" /></template>{{ t('log.clear') }}
      </n-button>
    </template>
  </ProTable>

  <n-drawer v-model:show="showDetail" :width="560" placement="right">
    <n-drawer-content :title="t('log.detail')" closable>
      <n-descriptions v-if="detailRow" label-placement="left" :column="1" bordered size="small">
        <n-descriptions-item :label="t('log.opName')">{{ detailRow.title }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.method')">{{ detailRow.httpMethod }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.path')">{{ detailRow.path }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.result')">
          <n-tag :type="detailRow.success ? 'success' : 'error'" size="small" :bordered="false">
            {{ detailRow.success ? t('log.success') : t('log.failed') }}
          </n-tag>
        </n-descriptions-item>
        <n-descriptions-item :label="t('log.resultCode')">{{ detailRow.resultCode }}</n-descriptions-item>
        <n-descriptions-item v-if="detailRow.exceptionMessage" :label="t('log.exception')">
          <pre class="param-json exception">{{ detailRow.exceptionMessage }}</pre>
        </n-descriptions-item>
        <n-descriptions-item :label="t('log.elapsed')">{{ detailRow.elapsedMs }} ms</n-descriptions-item>
        <n-descriptions-item :label="t('log.operator')">{{ operatorText(detailRow) }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.ip')">{{ detailRow.ip || '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.userAgent')">{{ detailRow.userAgent || '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('common.createTime')">{{ detailRow.createTime }}</n-descriptions-item>
        <n-descriptions-item :label="t('log.param')">
          <pre class="param-json">{{ prettyParam(detailRow.paramJson) }}</pre>
        </n-descriptions-item>
      </n-descriptions>
    </n-drawer-content>
  </n-drawer>
</template>

<style scoped>
.param-json {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-all;
  font-family: var(--font-mono, ui-monospace, monospace);
  font-size: 12px;
  line-height: 1.5;
}
.exception {
  color: var(--color-error, #d03050);
}
</style>
