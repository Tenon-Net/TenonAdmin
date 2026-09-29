<script setup lang="ts">
// 出站调用记录 = 只读 ProTable + 详情抽屉 + 出站目标清单。记录不含请求/响应正文与任何请求头(出站秘密只在请求头里);
// 地址不含查询串。支持从「可靠投递」详情带 ?deliveryId= / ?callId= 深链进入(刷新保持筛选)。
import { computed, h, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NAlert, NButton, NDataTable, NDescriptions, NDescriptionsItem, NDrawer, NDrawerContent, NSpace, NTag, useMessage,
  type DataTableColumns,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn } from 'tenon-naive-pro-table'
import { integrationOutboundLogApi } from '@/api/integration'
import { useAuthStore } from '@/stores/auth'
import { translateError } from '@/utils/error'
import type { OutboundLogRow, OutboundTarget } from '@/types/integration'
import { PERM, enumLabel, fmtDateTime, methodTagType, outboundAuthTypeKey, outboundOutcomeTag, queryText } from '../shared'

const { t } = useI18n()
const message = useMessage()
const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

/** 深链筛选:投递详情带入 deliveryId / callId;清除即回到全部。 */
const linkedDeliveryId = computed(() => queryText(route.query, 'deliveryId'))
const linkedCallId = computed(() => queryText(route.query, 'callId'))
const tableParams = computed(() => ({
  ...(linkedDeliveryId.value ? { deliveryId: linkedDeliveryId.value } : {}),
  ...(linkedCallId.value ? { callId: linkedCallId.value } : {}),
}))
const clearLink = (key: string) => {
  const query = { ...route.query }
  delete query[key]
  void router.replace({ path: route.path, query })
}

const outcomeOptions = Object.entries(outboundOutcomeTag).map(([value, tag]) => ({
  label: () => t(tag.key),
  value: Number(value),
  tagType: tag.type,
}))

const columns: ProTableColumn<OutboundLogRow>[] = [
  { key: 'createTime', title: () => t('integration.outboundLog.time'), format: 'datetime', width: 170, search: { type: 'daterange' } },
  { key: 'target', title: () => t('integration.outboundLog.target'), width: 130, search: true },
  { key: 'operation', title: () => t('integration.outboundLog.operation'), width: 140, search: true, render: (r) => r.operation || '—' },
  {
    key: 'httpMethod',
    title: () => t('integration.outboundLog.method'),
    width: 90,
    render: (r) => h(NTag, { size: 'small', bordered: false, type: methodTagType(r.httpMethod) }, () => r.httpMethod),
  },
  { key: 'url', title: () => t('integration.outboundLog.url'), ellipsis: { tooltip: true } },
  { key: 'statusCode', title: () => t('integration.outboundLog.status'), width: 100, render: (r) => (r.statusCode == null ? '—' : String(r.statusCode)) },
  {
    key: 'outcome',
    title: () => t('integration.outboundLog.outcome'),
    width: 110,
    tag: true,
    options: outcomeOptions,
    search: { props: { clearable: true } },
  },
  { key: 'reason', title: () => t('integration.outboundLog.reason'), width: 150, ellipsis: { tooltip: true }, render: (r) => r.reason || '—' },
  { key: 'elapsedMs', title: () => t('integration.outboundLog.elapsed'), width: 100, render: (r) => `${r.elapsedMs} ms` },
  { key: 'callId', title: () => t('integration.outboundLog.callId'), hideInTable: true, search: true },
  { key: 'traceId', title: () => t('integration.outboundLog.traceId'), hideInTable: true, search: true },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 90,
    hideInSetting: true,
    render: (r) => h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openDetail(r) }, () => t('integration.outboundLog.detail')),
  },
]

const showDetail = ref(false)
const detailRow = ref<OutboundLogRow | null>(null)
function openDetail(r: OutboundLogRow) {
  detailRow.value = r
  showDetail.value = true
}

// ── 出站目标清单(只回答是否取得到凭据,永不显示凭据)──
const showTargets = ref(false)
const targets = ref<OutboundTarget[]>([])
const targetsLoading = ref(false)
async function openTargets() {
  showTargets.value = true
  targetsLoading.value = true
  try {
    targets.value = await integrationOutboundLogApi.targets()
  } catch (e) {
    message.error(translateError(e))
  } finally {
    targetsLoading.value = false
  }
}
const targetColumns = computed<DataTableColumns<OutboundTarget>>(() => [
  { key: 'name', title: t('integration.outboundLog.targetName'), width: 140 },
  { key: 'baseUrl', title: t('integration.outboundLog.baseUrl'), ellipsis: { tooltip: true } },
  { key: 'timeoutSeconds', title: t('integration.outboundLog.timeout'), width: 80, render: (r) => `${r.timeoutSeconds}s` },
  {
    key: 'authType',
    title: t('integration.outboundLog.auth'),
    width: 150,
    render: (r) => {
      const label = t(outboundAuthTypeKey[Number(r.authType ?? 0)] ?? 'integration.outbound.authNone')
      return r.authHeaderName ? `${label}(${r.authHeaderName})` : label
    },
  },
  {
    key: 'trustedCidrs',
    title: t('integration.outboundLog.trustedCidrs'),
    width: 180,
    render: (r) =>
      r.trustedCidrs?.length
        ? h(NSpace, { size: 4 }, () => r.trustedCidrs!.map((c) => h(NTag, { size: 'small', bordered: false }, () => c)))
        : t('integration.outboundLog.noTrusted'),
  },
  {
    key: 'hasCredential',
    title: t('integration.outboundLog.credential'),
    width: 90,
    render: (r) =>
      h(NTag, { size: 'small', bordered: false, type: r.hasCredential ? 'success' : 'default' }, () =>
        t(r.hasCredential ? 'integration.outboundLog.credentialSet' : 'integration.outboundLog.credentialMissing'),
      ),
  },
])
</script>

<template>
  <ProTable
    :columns="columns"
    :fetcher="integrationOutboundLogApi.page"
    :params="tableParams"
    storage-key="integration-outbound-log"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-space :size="8">
        <n-button v-if="authStore.hasPerm(PERM.outboundTargets)" data-testid="outbound-targets" @click="openTargets">
          {{ t('integration.outboundLog.targets') }}
        </n-button>
        <n-tag v-if="linkedDeliveryId" closable type="info" :bordered="false" @close="clearLink('deliveryId')">
          {{ t('integration.outboundLog.filterDelivery') }}:{{ linkedDeliveryId }}
        </n-tag>
        <n-tag v-if="linkedCallId" closable type="info" :bordered="false" @close="clearLink('callId')">
          {{ t('integration.outboundLog.filterCall') }}:{{ linkedCallId }}
        </n-tag>
      </n-space>
    </template>
    <template #empty>{{ t('integration.outboundLog.empty') }}</template>
  </ProTable>

  <n-drawer v-model:show="showDetail" :width="560" placement="right">
    <n-drawer-content :title="t('integration.outboundLog.detail')" closable>
      <n-descriptions v-if="detailRow" :column="1" label-placement="left" bordered size="small">
        <n-descriptions-item :label="t('integration.outboundLog.time')">{{ fmtDateTime(detailRow.createTime) }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.outboundLog.target')">{{ detailRow.target }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.outboundLog.operation')">{{ detailRow.operation || '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.outboundLog.method')">{{ detailRow.httpMethod }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.outboundLog.url')">{{ detailRow.url }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.outboundLog.status')">{{ detailRow.statusCode ?? '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.outboundLog.outcome')">{{ enumLabel(t, outboundOutcomeTag, detailRow.outcome) }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.outboundLog.reason')">{{ detailRow.reason || '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.outboundLog.errorSummary')">{{ detailRow.errorSummary || '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.outboundLog.elapsed')">{{ detailRow.elapsedMs }} ms</n-descriptions-item>
        <n-descriptions-item :label="t('integration.outboundLog.callId')">{{ detailRow.callId }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.outboundLog.traceId')">{{ detailRow.traceId || '—' }}</n-descriptions-item>
        <n-descriptions-item :label="t('integration.outboundLog.delivery')">
          {{ detailRow.deliveryId ? `${detailRow.deliveryId} · #${detailRow.attemptNo ?? '—'}` : '—' }}
        </n-descriptions-item>
      </n-descriptions>
    </n-drawer-content>
  </n-drawer>

  <n-drawer v-model:show="showTargets" :width="860" placement="right">
    <n-drawer-content :title="t('integration.outboundLog.targetsTitle')" closable>
      <n-alert type="info" :show-icon="false" style="margin-bottom: 12px">{{ t('integration.outboundLog.targetsHint') }}</n-alert>
      <n-data-table :columns="targetColumns" :data="targets" :loading="targetsLoading" :row-key="(r: OutboundTarget) => r.name ?? ''" size="small" />
    </n-drawer-content>
  </n-drawer>
</template>
