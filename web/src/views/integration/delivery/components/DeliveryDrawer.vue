<script setup lang="ts">
// 投递详情抽屉:记录全貌 + 服务端判定的可用操作 + 尝试时间线。
// 按钮先按权限显示,是否可点只看服务端返回的 allowedActions——页面不自行判断「这次重试安全不安全」;
// 执行时回传页面看到的栅栏值;记录已被投递器或他人改动(49046)或状态已不允许(49045)时关掉操作弹窗并刷新,
// 由操作人按最新状态重新判断。
import { computed, h, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  NAlert, NButton, NDataTable, NDescriptions, NDescriptionsItem, NDrawer, NDrawerContent, NEmpty, NForm, NFormItem,
  NInput, NSpace, NSpin, NTag, useMessage, type DataTableColumns,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import CodeBlock from '@/components/CodeBlock/index.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import { useAuthStore } from '@/stores/auth'
import { ApiError } from '@/api'
import { integrationDeliveryApi } from '@/api/integration'
import { translateError } from '@/utils/error'
import {
  DeliveryActions, DeliveryStatus, type DeliveryAction, type DeliveryAttempt, type DeliveryDetail,
} from '@/types/integration'
import {
  DELIVERY_ACTION_PERM, PERM, attemptKindKey, attemptOutcomeKey, attemptTriggerKey, deliveryActionKey, deliveryStatusHint,
  deliveryStatusTag, enumLabel, fmtDateTime, outboundOutcomeTag,
} from '../../shared'

const emit = defineEmits<{ changed: [] }>()
const { t, te } = useI18n()
const message = useMessage()
const router = useRouter()
const authStore = useAuthStore()

const show = ref(false)
const loading = ref(false)
const detail = ref<DeliveryDetail | null>(null)
let currentId: number | string = 0
let loadSeq = 0

/** 取详情:只认最后一次请求且仍是当前记录的响应,快速切换记录时迟到的旧响应一律丢弃。 */
async function load(id: number | string) {
  const seq = ++loadSeq
  loading.value = true
  try {
    const loaded = await integrationDeliveryApi.get(id)
    if (seq === loadSeq && id === currentId) detail.value = loaded
  } catch (e) {
    if (seq === loadSeq) message.error(translateError(e))
  } finally {
    if (seq === loadSeq) loading.value = false
  }
}
const refresh = () => void load(currentId)

function open(id: number | string) {
  currentId = id
  detail.value = null
  show.value = true
  void load(id)
}
defineExpose({ open })

const statusTag = computed(() => (detail.value ? deliveryStatusTag[Number(detail.value.status)] : undefined))
const statusHint = computed(() => {
  const key = deliveryStatusHint[Number(detail.value?.status ?? -1)]
  return key ? t(key) : null
})
const hintType = computed(() => (detail.value?.status === DeliveryStatus.AwaitingConfirmation || detail.value?.status === DeliveryStatus.Dispatching ? 'info' : 'warning'))

/** 按钮:有权限才显示;是否可点由服务端 allowedActions 决定。 */
const actionOrder: DeliveryAction[] = [
  DeliveryActions.Retry, DeliveryActions.Query, DeliveryActions.ConfirmSucceeded, DeliveryActions.ConfirmNotExecuted, DeliveryActions.Cancel,
]
const actionHintKey: Record<DeliveryAction, string> = {
  [DeliveryActions.Retry]: 'integration.delivery.actionHintRetry',
  [DeliveryActions.Query]: 'integration.delivery.actionHintQuery',
  [DeliveryActions.ConfirmSucceeded]: 'integration.delivery.actionHintConfirmSucceeded',
  [DeliveryActions.ConfirmNotExecuted]: 'integration.delivery.actionHintConfirmNotExecuted',
  [DeliveryActions.Cancel]: 'integration.delivery.actionHintCancel',
}
const visibleActions = computed(() => actionOrder.filter((a) => authStore.hasPerm(DELIVERY_ACTION_PERM[a])))
const allowed = computed(() => new Set(detail.value?.allowedActions ?? []))
const needsNote = (a: DeliveryAction) => a === DeliveryActions.ConfirmSucceeded || a === DeliveryActions.ConfirmNotExecuted
/** 服务端判定「当前状态不允许」(49045)或「记录已被改动」(49046):页面所见已过期,须刷新后重新审视。 */
const STALE_VIEW_CODES = [49045, 49046]

const showAction = ref(false)
const pending = ref<DeliveryAction>(DeliveryActions.Retry)
const actionForm = reactive({ note: '' })
function openAction(action: DeliveryAction) {
  pending.value = action
  actionForm.note = ''
  showAction.value = true
}
async function doAction() {
  const target = detail.value
  if (!target?.id) return false
  const note = actionForm.note.trim()
  if (needsNote(pending.value) && !note) {
    message.warning(t('integration.delivery.noteRequired'))
    return false
  }
  try {
    // 目标 id 与栅栏取自同一份已加载的详情:不会把一条记录的栅栏发给另一条
    const updated = await integrationDeliveryApi.act(target.id, pending.value, { note: note || null, fence: target.fence ?? null })
    if (target.id === currentId) {
      ++loadSeq   // 作废操作前发出、尚未返回的刷新,免得旧响应盖掉操作结果
      loading.value = false
      detail.value = updated
    }
    message.success(t('integration.delivery.actionDone'))
    emit('changed')
  } catch (e) {
    message.error(translateError(e))
    if (e instanceof ApiError && STALE_VIEW_CODES.includes(e.code)) {
      void load(target.id)   // 关弹窗并刷新详情,操作人按最新状态重新判断,不带着新栅栏静默重发
      return
    }
    return false   // 其他错误(如 49047 缺说明)留在弹窗里修改
  }
}

function viewCalls(query: Record<string, string>) {
  show.value = false
  void router.push({ path: '/integration/outbound-log', query })
}

const outcomeText = (value?: string | null) => {
  if (!value) return '—'
  const key = attemptOutcomeKey[value]
  return key && te(key) ? t(key) : value
}
const payloadText = computed(() => {
  const raw = detail.value?.payloadJson
  if (!raw) return ''
  try {
    return JSON.stringify(JSON.parse(raw), null, 2)
  } catch {
    return raw
  }
})
const capabilityTags = computed(() => {
  const d = detail.value
  if (!d) return []
  if (!d.adapterRegistered) return [{ text: t('integration.delivery.adapterMissing'), type: 'error' as const }]
  const tags: { text: string; type: 'success' | 'info' | 'default' }[] = []
  if (d.supportsIdempotency) tags.push({ text: t('integration.delivery.capIdempotency'), type: 'success' })
  if (d.supportsQuery) tags.push({ text: t('integration.delivery.capQuery'), type: 'info' })
  if (!tags.length) tags.push({ text: t('integration.delivery.capNone'), type: 'default' })
  return tags
})

const attemptColumns = computed<DataTableColumns<DeliveryAttempt>>(() => [
  { key: 'startedAt', title: t('integration.delivery.attemptTime'), width: 160, render: (r) => fmtDateTime(r.startedAt) },
  { key: 'attemptNo', title: t('integration.delivery.attemptNo'), width: 60 },
  { key: 'kind', title: t('integration.delivery.attemptKind'), width: 90, render: (r) => enumLabel(t, attemptKindKey, r.kind) },
  { key: 'trigger', title: t('integration.delivery.attemptTrigger'), width: 90, render: (r) => enumLabel(t, attemptTriggerKey, r.trigger) },
  { key: 'outcome', title: t('integration.delivery.attemptOutcome'), width: 120, render: (r) => outcomeText(r.outcome) },
  { key: 'httpStatus', title: t('integration.delivery.httpStatus'), width: 90, render: (r) => (r.httpStatus == null ? '—' : String(r.httpStatus)) },
  {
    key: 'callId',
    title: t('integration.delivery.callId'),
    width: 110,
    render: (r) =>
      r.callId && authStore.hasPerm(PERM.outboundLog)
        ? h(NButton, { text: true, type: 'primary', size: 'small', onClick: () => viewCalls({ callId: r.callId! }) }, () => r.callId!.slice(0, 8))
        : r.callId ? r.callId.slice(0, 8) : '—',
  },
  { key: 'operatorName', title: t('integration.delivery.operator'), width: 100, render: (r) => r.operatorName || '—' },
  { key: 'note', title: t('integration.delivery.attemptNote'), ellipsis: { tooltip: true }, render: (r) => r.note || '—' },
  { key: 'errorSummary', title: t('integration.delivery.errorSummary'), ellipsis: { tooltip: true }, render: (r) => r.errorSummary || '—' },
])
</script>

<template>
  <n-drawer v-model:show="show" :width="900" placement="right">
    <n-drawer-content closable>
      <template #header>
        <n-space :size="8" align="center">
          <span>{{ t('integration.delivery.detail') }}</span>
          <n-tag v-if="statusTag" size="small" :bordered="false" :type="statusTag.type" data-testid="delivery-status">{{ t(statusTag.key) }}</n-tag>
        </n-space>
      </template>
      <n-spin :show="loading">
        <template v-if="detail">
          <n-alert v-if="statusHint" :type="hintType" :show-icon="false" style="margin-bottom: 12px" data-testid="delivery-hint">{{ statusHint }}</n-alert>

          <n-space :size="8" style="margin-bottom: 12px">
            <n-button
              v-for="a in visibleActions"
              :key="a"
              size="small"
              :type="a === DeliveryActions.Cancel ? 'default' : 'primary'"
              :secondary="a !== DeliveryActions.Retry"
              :disabled="!allowed.has(a)"
              :data-testid="`delivery-action-${a}`"
              @click="openAction(a)"
            >
              {{ t(deliveryActionKey[a]) }}
            </n-button>
            <n-button v-if="authStore.hasPerm(PERM.outboundLog)" size="small" quaternary @click="viewCalls({ deliveryId: String(detail.id) })">
              {{ t('integration.delivery.viewCalls') }}
            </n-button>
            <n-button size="small" quaternary @click="refresh">{{ t('integration.delivery.refresh') }}</n-button>
          </n-space>

          <n-descriptions :column="2" label-placement="left" bordered size="small">
            <n-descriptions-item :label="t('integration.delivery.deliveryKey')" :span="2">
              <span data-testid="delivery-key">{{ detail.deliveryKey }}</span>
            </n-descriptions-item>
            <n-descriptions-item :label="t('integration.delivery.adapter')">{{ detail.adapter }}</n-descriptions-item>
            <n-descriptions-item :label="t('integration.delivery.capabilities')">
              <n-space :size="4">
                <n-tag v-for="c in capabilityTags" :key="c.text" size="small" :bordered="false" :type="c.type">{{ c.text }}</n-tag>
              </n-space>
            </n-descriptions-item>
            <n-descriptions-item :label="t('integration.delivery.operation')">{{ detail.operation }}</n-descriptions-item>
            <n-descriptions-item :label="t('integration.delivery.businessKey')">{{ detail.businessKey || '—' }}</n-descriptions-item>
            <n-descriptions-item :label="t('integration.delivery.attempts')">
              {{ t('integration.delivery.attemptsValue', { count: detail.attemptsInBudget, max: detail.maxAttempts }) }}
              <span class="field-hint">· {{ t('integration.delivery.attemptsTotal', { n: detail.attemptCount }) }}</span>
            </n-descriptions-item>
            <n-descriptions-item :label="t('integration.delivery.nextAttempt')">{{ fmtDateTime(detail.nextAttemptAt) }}</n-descriptions-item>
            <n-descriptions-item :label="t('integration.delivery.lastOutcome')">
              {{ enumLabel(t, outboundOutcomeTag, detail.lastOutcome) }}
            </n-descriptions-item>
            <n-descriptions-item :label="t('integration.delivery.remoteReference')">{{ detail.remoteReference || '—' }}</n-descriptions-item>
            <n-descriptions-item :label="t('integration.delivery.lastError')" :span="2">{{ detail.lastError || '—' }}</n-descriptions-item>
            <n-descriptions-item :label="t('integration.delivery.createTime')">{{ fmtDateTime(detail.createTime) }}</n-descriptions-item>
            <n-descriptions-item :label="t('integration.delivery.completedAt')">{{ fmtDateTime(detail.completedAt) }}</n-descriptions-item>
            <n-descriptions-item :label="t('integration.delivery.deadline')">{{ fmtDateTime(detail.deadlineAt) }}</n-descriptions-item>
            <n-descriptions-item :label="t('integration.delivery.confirmDeadline')">{{ fmtDateTime(detail.confirmDeadlineAt) }}</n-descriptions-item>
            <n-descriptions-item v-if="detail.leaseUntil" :label="t('integration.delivery.lease')" :span="2">
              {{ detail.leaseOwner || '—' }} · {{ fmtDateTime(detail.leaseUntil) }}
            </n-descriptions-item>
            <n-descriptions-item v-if="detail.resolutionNote" :label="t('integration.delivery.resolutionNote')" :span="2">
              {{ detail.resolutionNote }}
            </n-descriptions-item>
          </n-descriptions>

          <h4 class="section-title">{{ t('integration.delivery.payload') }}</h4>
          <CodeBlock v-if="payloadText" :code="payloadText" />

          <h4 class="section-title">{{ t('integration.delivery.attempts') }}</h4>
          <n-data-table
            v-if="detail.attempts?.length"
            :columns="attemptColumns"
            :data="detail.attempts"
            :row-key="(r: DeliveryAttempt) => String(r.id)"
            size="small"
            :scroll-x="1100"
            data-testid="delivery-attempts"
          />
          <n-empty v-else :description="t('integration.delivery.noAttempts')" />
        </template>
      </n-spin>
    </n-drawer-content>
  </n-drawer>

  <FormContainer
    v-model:show="showAction"
    variant="modal"
    :title="t(deliveryActionKey[pending])"
    :width="480"
    :on-confirm="doAction"
    :confirm-text="t(deliveryActionKey[pending])"
  >
    <n-alert type="info" :show-icon="false" style="margin-bottom: 12px">{{ t(actionHintKey[pending]) }}</n-alert>
    <n-form :model="actionForm" label-placement="top">
      <n-form-item :label="t('integration.delivery.note')" :required="needsNote(pending)">
        <n-input
          v-model:value="actionForm.note"
          type="textarea"
          :maxlength="256"
          show-count
          :placeholder="t('integration.delivery.notePlaceholder')"
          data-testid="delivery-action-note"
        />
      </n-form-item>
    </n-form>
  </FormContainer>
</template>

<style scoped>
.section-title {
  margin: 16px 0 8px;
  font-size: 14px;
  font-weight: 600;
}

.field-hint {
  color: var(--n-text-color-3, var(--text-3));
  font-size: 12px;
}
</style>
