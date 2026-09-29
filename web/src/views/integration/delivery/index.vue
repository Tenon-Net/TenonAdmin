<script setup lang="ts">
// 可靠投递 = 状态筛选条(带计数)+ ProTable + 详情抽屉。状态语义分明:已受理待确认 ≠ 成功;待核对 = 结果未知且不能自动重发;
// 重试耗尽、失败需要人工处理。支持 ?deliveryKey= / ?businessKey= 深链(业务页可直接跳到自己的投递)。
import { computed, h, onActivated, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NBadge, NButton, NSpace, NTag, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { ProTable, type ProTableColumn } from 'tenon-naive-pro-table'
import { integrationDeliveryApi } from '@/api/integration'
import { useAuthStore } from '@/stores/auth'
import { translateError } from '@/utils/error'
import { DeliveryStatus, type DeliveryRow } from '@/types/integration'
import DeliveryDrawer from './components/DeliveryDrawer.vue'
import { PERM, deliveryStatusTag, enumLabel, fmtDateTime, outboundOutcomeTag, queryText } from '../shared'

const { t } = useI18n()
const message = useMessage()
const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

const tableRef = ref<{ refresh: () => void } | null>(null)
const drawerRef = ref<InstanceType<typeof DeliveryDrawer> | null>(null)

// ── 状态筛选条:计数来自服务端汇总(有权限才取),每次表格加载后(@loaded)刷新;点击即筛选 ──
const statusOrder = [
  DeliveryStatus.NeedsReconciliation, DeliveryStatus.Exhausted, DeliveryStatus.Failed, DeliveryStatus.AwaitingConfirmation,
  DeliveryStatus.Pending, DeliveryStatus.Dispatching, DeliveryStatus.Succeeded, DeliveryStatus.Cancelled,
]
const counts = ref<Record<number, number>>({})
async function loadSummary() {
  if (!authStore.hasPerm(PERM.deliverySummary)) return
  try {
    const rows = await integrationDeliveryApi.summary()
    counts.value = Object.fromEntries(rows.map((r) => [Number(r.status), Number(r.count)]))
  } catch (e) {
    message.error(translateError(e))
  }
}
onActivated(loadSummary)

/** 深链 ?status= 只认已知状态值,非法值忽略(不把 NaN 发给后端)。 */
const statusFilter = computed(() => {
  const text = queryText(route.query, 'status')
  const v = text == null ? Number.NaN : Number(text)
  return deliveryStatusTag[v] ? v : null
})
/** 深链带入的投递标识 / 业务键优先于搜索表单,所以以可关闭的标签显示出来,关掉即回到表单筛选。 */
const linkedDeliveryKey = computed(() => queryText(route.query, 'deliveryKey'))
const linkedBusinessKey = computed(() => queryText(route.query, 'businessKey'))
function setQuery(key: string, value: string | null) {
  const query = { ...route.query }
  if (value == null) delete query[key]
  else query[key] = value
  void router.replace({ path: route.path, query })
}
const setStatus = (status: number | null) => setQuery('status', status == null ? null : String(status))
const tableParams = computed(() => ({
  ...(statusFilter.value != null ? { status: statusFilter.value } : {}),
  ...(linkedDeliveryKey.value ? { deliveryKey: linkedDeliveryKey.value } : {}),
  ...(linkedBusinessKey.value ? { businessKey: linkedBusinessKey.value } : {}),
}))

const columns: ProTableColumn<DeliveryRow>[] = [
  { key: 'createTime', title: () => t('integration.delivery.createTime'), format: 'datetime', width: 170, search: { type: 'daterange' } },
  { key: 'deliveryKey', title: () => t('integration.delivery.deliveryKey'), minWidth: 200, ellipsis: { tooltip: true }, search: true },
  { key: 'adapter', title: () => t('integration.delivery.adapter'), width: 170, search: true },
  { key: 'operation', title: () => t('integration.delivery.operation'), width: 130, search: true },
  { key: 'businessKey', title: () => t('integration.delivery.businessKey'), width: 150, ellipsis: { tooltip: true }, search: true, render: (r) => r.businessKey || '—' },
  {
    key: 'status',
    title: () => t('integration.delivery.status'),
    width: 130,
    render: (r) => {
      const tag = deliveryStatusTag[Number(r.status)]
      return tag ? h(NTag, { size: 'small', bordered: false, type: tag.type }, () => t(tag.key)) : '—'
    },
  },
  {
    key: 'attemptCount',
    title: () => t('integration.delivery.attempts'),
    width: 90,
    render: (r) => t('integration.delivery.attemptsValue', { count: r.attemptsInBudget, max: r.maxAttempts }),
  },
  { key: 'nextAttemptAt', title: () => t('integration.delivery.nextAttempt'), width: 170, render: (r) => fmtDateTime(r.nextAttemptAt) },
  { key: 'lastOutcome', title: () => t('integration.delivery.lastOutcome'), width: 110, render: (r) => enumLabel(t, outboundOutcomeTag, r.lastOutcome) },
  { key: 'lastError', title: () => t('integration.delivery.lastError'), ellipsis: { tooltip: true }, render: (r) => r.lastError || '—' },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 90,
    fixed: 'right',
    hideInSetting: true,
    render: (r) =>
      authStore.hasPerm(PERM.deliveryGet)
        ? h(NButton, { size: 'small', quaternary: true, type: 'primary', 'data-testid': 'delivery-detail', onClick: () => drawerRef.value?.open(r.id!) }, () => t('integration.delivery.detail'))
        : null,
  },
]

function onChanged() {
  tableRef.value?.refresh()
}
</script>

<template>
  <ProTable
    ref="tableRef"
    :columns="columns"
    :fetcher="integrationDeliveryApi.page"
    :params="tableParams"
    storage-key="integration-delivery"
    @loaded="loadSummary"
    @error="(e) => message.error(translateError(e))"
  >
    <template #toolbar>
      <n-space :size="8" align="center">
        <n-space :size="6" data-testid="delivery-status-filter">
          <n-button size="small" :type="statusFilter == null ? 'primary' : 'default'" :secondary="statusFilter != null" @click="setStatus(null)">
            {{ t('integration.delivery.all') }}
          </n-button>
          <n-badge v-for="s in statusOrder" :key="s" :value="counts[s] ?? 0" :show="(counts[s] ?? 0) > 0" :max="999" :type="deliveryStatusTag[s].type === 'error' ? 'error' : deliveryStatusTag[s].type === 'warning' ? 'warning' : 'info'">
            <n-button size="small" :type="statusFilter === s ? 'primary' : 'default'" :secondary="statusFilter !== s" @click="setStatus(s)">
              {{ t(deliveryStatusTag[s].key) }}
            </n-button>
          </n-badge>
        </n-space>
        <n-tag v-if="linkedDeliveryKey" closable type="info" :bordered="false" @close="setQuery('deliveryKey', null)">
          {{ t('integration.delivery.deliveryKey') }}:{{ linkedDeliveryKey }}
        </n-tag>
        <n-tag v-if="linkedBusinessKey" closable type="info" :bordered="false" @close="setQuery('businessKey', null)">
          {{ t('integration.delivery.businessKey') }}:{{ linkedBusinessKey }}
        </n-tag>
      </n-space>
    </template>
    <template #empty>{{ t('integration.delivery.empty') }}</template>
  </ProTable>

  <DeliveryDrawer ref="drawerRef" @changed="onChanged" />
</template>
