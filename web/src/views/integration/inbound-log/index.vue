<script setup lang="ts">
// 开放调用记录 = 只读 ProTable + 详情抽屉。记录不含请求/响应正文与任何认证头;凭据只以公开标识出现。
// 支持从接入应用页带 ?appId= 深链进入(刷新保持筛选)。
import { computed, h, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton, NDescriptions, NDescriptionsItem, NDrawer, NDrawerContent, NTag, useMessage,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn } from 'tenon-naive-pro-table'
import { integrationAppApi, integrationInboundLogApi } from '@/api/integration'
import { useAuthStore } from '@/stores/auth'
import { translateError } from '@/utils/error'
import type { InboundLogRow } from '@/types/integration'
import { PERM, enumLabel, fmtDateTime, inboundOutcomeTag, methodTagType, queryText } from '../shared'

const { t } = useI18n()
const message = useMessage()
const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

/** 应用下拉要「接入应用-分页」权限;没有就不取、也不给按应用筛选(深链带入的 ?appId= 仍然生效)。 */
const canListApps = authStore.hasPerm(PERM.appPage)
const appOptions = ref<{ label: string; value: string }[]>([])
onMounted(async () => {
  if (!canListApps) return
  try {
    const page = await integrationAppApi.page({ page: 1, pageSize: 200 })
    appOptions.value = page.items.map((a) => ({ label: `${a.name}(${a.code})`, value: String(a.id) }))
  } catch (e) {
    message.error(translateError(e))
  }
})

/** 深链筛选:?appId= 由接入应用页「调用记录」带入;清除即回到全部。 */
const linkedAppId = computed(() => queryText(route.query, 'appId'))
const tableParams = computed(() => (linkedAppId.value ? { appId: linkedAppId.value } : {}))
const linkedAppLabel = computed(() => appOptions.value.find((o) => o.value === linkedAppId.value)?.label ?? linkedAppId.value)
const clearLink = () => void router.replace({ path: route.path })

const outcomeOptions = Object.entries(inboundOutcomeTag).map(([value, tag]) => ({
  label: () => t(tag.key),
  value: Number(value),
  tagType: tag.type,
}))

const columns: ProTableColumn<InboundLogRow>[] = [
  { key: 'createTime', title: () => t('integration.inboundLog.time'), format: 'datetime', width: 170, search: { type: 'daterange' } },
  {
    key: 'appId',
    title: () => t('integration.inboundLog.app'),
    options: appOptions,
    search: canListApps ? { props: { clearable: true, filterable: true } } : false,
    render: (r) => r.appCode || '—',
  },
  {
    key: 'httpMethod',
    title: () => t('integration.inboundLog.method'),
    width: 90,
    render: (r) => h(NTag, { size: 'small', bordered: false, type: methodTagType(r.httpMethod) }, () => r.httpMethod),
  },
  { key: 'route', title: () => t('integration.inboundLog.route'), ellipsis: { tooltip: true }, search: true, render: (r) => r.route || r.path },
  { key: 'statusCode', title: () => t('integration.inboundLog.status'), width: 90 },
  { key: 'resultCode', title: () => t('integration.inboundLog.resultCode'), width: 100 },
  {
    key: 'outcome',
    title: () => t('integration.inboundLog.outcome'),
    width: 110,
    tag: true,
    options: outcomeOptions,
    search: { props: { clearable: true } },
  },
  { key: 'failureReason', title: () => t('integration.inboundLog.reason'), ellipsis: { tooltip: true }, render: (r) => r.failureReason || '—' },
  { key: 'elapsedMs', title: () => t('integration.inboundLog.elapsed'), width: 100, render: (r) => `${r.elapsedMs} ms` },
  { key: 'traceId', title: () => t('integration.inboundLog.traceId'), ellipsis: { tooltip: true }, search: true },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 90,
    hideInSetting: true,
    render: (r) => h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openDetail(r) }, () => t('integration.inboundLog.detail')),
  },
]

const showDetail = ref(false)
const detailRow = ref<InboundLogRow | null>(null)
function openDetail(r: InboundLogRow) {
  detailRow.value = r
  showDetail.value = true
}
</script>

<template>
  <ProTable
    :columns="columns"
    :fetcher="integrationInboundLogApi.page"
    :params="tableParams"
    storage-key="integration-inbound-log"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-tag v-if="linkedAppId" closable type="info" :bordered="false" @close="clearLink">
        {{ t('integration.inboundLog.app') }}:{{ linkedAppLabel }}
      </n-tag>
    </template>
    <template #empty>{{ t('integration.inboundLog.empty') }}</template>
  </ProTable>

  <n-drawer v-model:show="showDetail" :width="560" placement="right">
    <n-drawer-content :title="t('integration.inboundLog.detail')" closable>
      <n-descriptions v-if="detailRow" :column="1" label-placement="left" bordered size="small">
        <n-descriptions-item :label="t('integration.inboundLog.time')">{{ fmtDateTime(detailRow.createTime) }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.inboundLog.app')">{{ detailRow.appCode || '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.inboundLog.keyId')">{{ detailRow.keyId ? `tna_${detailRow.keyId}.****` : '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.inboundLog.route')">{{ detailRow.route || '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.inboundLog.path')">{{ detailRow.path }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.inboundLog.status')">{{ detailRow.statusCode }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.inboundLog.resultCode')">{{ detailRow.resultCode }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.inboundLog.outcome')">{{ enumLabel(t, inboundOutcomeTag, detailRow.outcome) }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.inboundLog.reason')">{{ detailRow.failureReason || '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.inboundLog.elapsed')">{{ detailRow.elapsedMs }} ms</n-descriptions-item>
        <n-descriptions-item :label="t('integration.inboundLog.traceId')">{{ detailRow.traceId }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.inboundLog.requestId')">{{ detailRow.clientRequestId || '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.inboundLog.clientIp')">{{ detailRow.clientIp || '—' }}</n-descriptions-item>
      </n-descriptions>
    </n-drawer-content>
  </n-drawer>
</template>
