<script setup lang="ts">
/**
 * 审批实例详情:进度时间线 + 意见 + formComponent 挂载点 + 办理/发起人动词。
 * 约定式路由:views/workflow/instance/detail.vue → /workflow/instance/:id/detail。
 */
import { computed, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton,
  NCard,
  NDescriptions,
  NDescriptionsItem,
  NEmpty,
  NInput,
  NModal,
  NSelect,
  NSpace,
  NTag,
  NTimeline,
  NTimelineItem,
  useMessage,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import DetailPage from '@/components/DetailPage/index.vue'
import UserSelect from '@/components/UserSelect/index.vue'
import { useTabTitle } from '@/composables/useTabTitle'
import { useConfirm } from '@/composables/useConfirm'
import { useTabsStore } from '@/stores/tabs'
import { useUserStore } from '@/stores/user'
import { wfInstanceApi, wfTaskApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import type { WfHisTask, WfInstanceDetail, WfInstanceStatus, WfTaskAction } from '@/types/workflow'
import type { WfButtonLabels, WfModel, WfReturnPolicy } from '@/workflow/schema'
import { findNode, flattenChain } from '@/workflow/model'
import WfFormMount from '../components/WfFormMount.vue'
import WfNodeTree from '../definition/components/WfNodeTree.vue'

type ActionKind = 'approve' | 'reject' | 'transfer' | 'return' | 'delegate' | 'urge' | 'cancel' | 'resubmit'

const props = defineProps<{ id?: number | string }>()
const emit = defineEmits<{ back: [] }>()

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const message = useMessage()
const setTabTitle = useTabTitle()
const tabs = useTabsStore()
const { run } = useConfirm()

const isInline = computed(() => props.id != null)
const uid = computed(() => Number(props.id ?? route.params.id))

const user = useUserStore()
const loading = ref(false)
const detail = ref<WfInstanceDetail | null>(null)

const replayModel = computed<WfModel | null>(() => {
  const model = detail.value?.model
  if (!model?.root?.id || !model.root.type) return null
  return model as WfModel
})

const visitedIds = computed(() => detail.value?.visitedNodeIds ?? [])
const currentIds = computed(() => detail.value?.currentNodeIds ?? [])

const actionShow = ref(false)
const actionKind = ref<ActionKind>('approve')
const actionForm = reactive({ comment: '', toUserId: null as number | null, targetNodeId: null as string | null })
const actionSubmitting = ref(false)

const isStarter = computed(() => {
  const starter = detail.value?.starterUserId
  const me = user.userInfo?.userId
  if (starter == null) return false
  if (me == null) return true
  return Number(starter) === Number(me)
})

const isRunning = computed(() => Number(detail.value?.status) === 1)

const hasApproveHistory = computed(() =>
  (detail.value?.hisTasks ?? []).some((item) => Number(item.action) === 1),
)

const currentNodeId = computed(() =>
  detail.value?.myPendingTask?.nodeId
  ?? detail.value?.currentNodeIds?.[0]
  ?? '',
)

const currentNode = computed(() => {
  const model = replayModel.value
  if (!model || !currentNodeId.value) return null
  return findNode(model.root, currentNodeId.value)
})

const buttonLabels = computed<WfButtonLabels>(() => currentNode.value?.props?.buttonLabels ?? {})

const returnPolicy = computed<WfReturnPolicy | undefined>(() => currentNode.value?.props?.returnPolicy)

const returnTargetOptions = computed(() => {
  const model = replayModel.value
  if (!model) return []
  const walked = new Set(detail.value?.visitedNodeIds ?? [])
  return flattenChain(model.root)
    .filter((n) => walked.has(n.id) && n.id !== currentNodeId.value)
    .map((n) => ({
      label: n.name?.trim() || t(`workflow.node.${n.type}`),
      value: n.id,
    }))
})

const canUrge = computed(() =>
  isStarter.value && isRunning.value && !!(detail.value?.myPendingTask?.taskId ?? detail.value?.currentTaskId),
)

const canCancel = computed(() => isStarter.value && isRunning.value && !hasApproveHistory.value)

const canResubmit = computed(() =>
  isStarter.value && isRunning.value && !detail.value?.myPendingTask,
)

function btnText(kind: keyof WfButtonLabels, fallbackKey: string) {
  const custom = buttonLabels.value[kind]?.trim()
  return custom || t(fallbackKey)
}

const title = computed(() => detail.value?.definitionName || t('workflow.detail.title'))

function statusLabel(s: WfInstanceStatus | undefined): string {
  const key = normalizeStatus(s)
  return t(`workflow.status.${key}`, String(s ?? ''))
}

function statusType(s: WfInstanceStatus | undefined): 'default' | 'info' | 'success' | 'error' | 'warning' {
  const key = normalizeStatus(s)
  if (key === 'approved') return 'success'
  if (key === 'rejected' || key === 'terminated') return 'error'
  if (key === 'cancelled') return 'warning'
  if (key === 'running') return 'info'
  return 'default'
}

function normalizeStatus(s: WfInstanceStatus | undefined): string {
  if (s == null) return 'unknown'
  const map: Record<number, string> = {
    1: 'running',
    2: 'approved',
    3: 'rejected',
    4: 'cancelled',
    5: 'terminated',
  }
  return map[s] ?? 'unknown'
}

function actionLabel(a: WfTaskAction | undefined): string {
  if (a == null) return t('workflow.action.unknown')
  const map: Record<number, string> = {
    1: 'approve',
    2: 'reject',
    3: 'transfer',
    4: 'return',
    5: 'withdraw',
    6: 'delegate',
  }
  const key = map[a] ?? 'unknown'
  return t(`workflow.action.${key}`, String(a))
}

function actionTagType(a: WfTaskAction | undefined): 'default' | 'success' | 'error' | 'warning' | 'info' {
  if (a === 1) return 'success'
  if (a === 2) return 'error'
  if (a === 3) return 'warning'
  return 'info'
}

function formatTime(v?: string | null) {
  if (!v) return '—'
  return v.replace('T', ' ').slice(0, 19)
}

function userFallback(id?: number | string | null, name?: string | null) {
  const n = name?.trim()
  if (n) return n
  if (id == null || id === '') return ''
  return t('workflow.detail.userFallback', { id })
}

function operatorText(item: WfHisTask) {
  const named = (item as WfHisTask & { operatorName?: string | null }).operatorName
  return userFallback(item.userId, named)
}

function transferText(item: WfHisTask) {
  if (item.transferToUserId == null) return ''
  const named = (item as WfHisTask & { transferToUserName?: string | null }).transferToUserName
  return userFallback(item.transferToUserId, named)
}

type VarPair = { key: string; value: string }

const variableRows = computed((): VarPair[] => {
  const json = detail.value?.variablesJson
  if (!json?.trim()) return []
  try {
    const parsed = JSON.parse(json) as unknown
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      return Object.entries(parsed as Record<string, unknown>).map(([key, value]) => ({
        key,
        value: typeof value === 'string' ? value : JSON.stringify(value),
      }))
    }
  } catch {
    return [{ key: '', value: json }]
  }
  return [{ key: '', value: json }]
})

async function load(id: number) {
  if (!Number.isFinite(id) || id <= 0) return
  loading.value = true
  try {
    detail.value = await wfInstanceApi.get(id)
    if (!isInline.value) setTabTitle(detail.value.definitionName ?? '')
  } catch (e) {
    message.error(translateError(e))
    detail.value = null
  } finally {
    loading.value = false
  }
}

watch(uid, (id) => void load(id), { immediate: true })

function onBack() {
  if (isInline.value) {
    emit('back')
    return
  }
  const listPath = '/workflow/todo'
  void router.push(listPath).then(() => tabs.removeTab(route.path))
}

function openAction(kind: ActionKind) {
  actionKind.value = kind
  actionForm.comment = ''
  actionForm.toUserId = null
  actionForm.targetNodeId = null
  actionShow.value = true
}

const actionTitle = computed(() => t(`workflow.detail.${actionKind.value}`))

async function submitAction() {
  const kind = actionKind.value
  if ((kind === 'transfer' || kind === 'delegate') && (!actionForm.toUserId || actionForm.toUserId <= 0)) {
    message.warning(t(kind === 'delegate' ? 'workflow.detail.delegateRequired' : 'workflow.detail.transferRequired'))
    return
  }
  if (kind === 'return' && returnPolicy.value === 'any' && !actionForm.targetNodeId) {
    message.warning(t('workflow.detail.returnTargetRequired'))
    return
  }

  actionSubmitting.value = true
  try {
    const taskId = Number(detail.value?.myPendingTask?.taskId ?? detail.value?.currentTaskId ?? 0)
    const body = {
      taskId,
      comment: actionForm.comment.trim() || null,
      toUserId: actionForm.toUserId ?? 0,
      targetNodeId: actionForm.targetNodeId,
    }
    const api = (): Promise<unknown> => {
      if (kind === 'approve') return wfTaskApi.approve(body)
      if (kind === 'reject') return wfTaskApi.reject(body)
      if (kind === 'transfer') return wfTaskApi.transfer(body)
      if (kind === 'return') return wfTaskApi.return(body)
      if (kind === 'delegate') return wfTaskApi.delegate(body)
      if (kind === 'urge') return wfTaskApi.urge({ taskId })
      if (kind === 'cancel') return wfInstanceApi.cancel({ instanceId: uid.value })
      return wfInstanceApi.resubmit({
        instanceId: uid.value,
        variablesJson: detail.value?.variablesJson ?? null,
      })
    }
    const ok = await run(api, t('common.success'))
    if (ok) {
      actionShow.value = false
      await load(uid.value)
    }
  } finally {
    actionSubmitting.value = false
  }
}

function timelineType(item: WfHisTask): 'default' | 'success' | 'error' | 'info' | 'warning' {
  const a = item.action
  if (a === 1) return 'success'
  if (a === 2) return 'error'
  if (a === 3) return 'warning'
  return 'info'
}
</script>

<template>
  <DetailPage :title="title" :loading="loading" @back="onBack">
    <template v-if="detail && (detail.myPendingTask || canUrge || canCancel || canResubmit)" #actions>
      <n-space>
        <n-button v-if="detail.myPendingTask" type="primary" @click="openAction('approve')">
          {{ btnText('approve', 'workflow.detail.approve') }}
        </n-button>
        <n-button v-if="detail.myPendingTask" type="error" @click="openAction('reject')">
          {{ btnText('reject', 'workflow.detail.reject') }}
        </n-button>
        <n-button v-if="detail.myPendingTask" @click="openAction('return')">
          {{ btnText('return', 'workflow.detail.return') }}
        </n-button>
        <n-button v-if="detail.myPendingTask" @click="openAction('transfer')">
          {{ btnText('transfer', 'workflow.detail.transfer') }}
        </n-button>
        <n-button v-if="detail.myPendingTask" @click="openAction('delegate')">
          {{ btnText('delegate', 'workflow.detail.delegate') }}
        </n-button>
        <n-button v-if="canUrge" @click="openAction('urge')">
          {{ btnText('urge', 'workflow.detail.urge') }}
        </n-button>
        <n-button v-if="canCancel" @click="openAction('cancel')">
          {{ t('workflow.detail.cancel') }}
        </n-button>
        <n-button v-if="canResubmit" type="primary" @click="openAction('resubmit')">
          {{ t('workflow.detail.resubmit') }}
        </n-button>
      </n-space>
    </template>

    <template v-if="detail">
      <n-card size="small" :bordered="false" :title="t('workflow.detail.summary')">
        <n-descriptions label-placement="left" :column="2" size="small">
          <n-descriptions-item :label="t('workflow.detail.definition')">
            {{ detail.definitionName }}
            <span v-if="detail.version"> (v{{ detail.version }})</span>
          </n-descriptions-item>
          <n-descriptions-item :label="t('common.status')">
            <n-tag size="small" :type="statusType(detail.status)" :bordered="false">
              {{ statusLabel(detail.status) }}
            </n-tag>
          </n-descriptions-item>
          <n-descriptions-item :label="t('workflow.detail.businessKey')">
            {{ detail.businessKey || '—' }}
          </n-descriptions-item>
          <n-descriptions-item :label="t('workflow.detail.createTime')">
            {{ formatTime(detail.createTime) }}
          </n-descriptions-item>
        </n-descriptions>
        <dl v-if="variableRows.length" class="wf-vars">
          <div v-for="(row, i) in variableRows" :key="row.key || i" class="wf-var-row">
            <dt>{{ row.key || t('workflow.detail.variables') }}</dt>
            <dd>{{ row.value }}</dd>
          </div>
        </dl>
      </n-card>

      <n-card
        v-if="replayModel"
        size="small"
        :bordered="false"
        :title="t('workflow.detail.replay')"
        style="margin-top: 12px"
      >
        <div class="wf-replay">
          <WfNodeTree
            :model="replayModel"
            readonly
            :visited-ids="visitedIds"
            :current-ids="currentIds"
          />
        </div>
      </n-card>

      <n-card
        v-if="detail.formComponent"
        size="small"
        :bordered="false"
        :title="t('workflow.detail.form')"
        style="margin-top: 12px"
      >
        <WfFormMount
          :form-component="detail.formComponent"
          mode="view"
          :definition-id="detail.definitionId == null ? undefined : Number(detail.definitionId)"
          :instance-id="detail.id == null ? undefined : Number(detail.id)"
          :business-key="detail.businessKey"
          :variables-json="detail.variablesJson"
          :status="detail.status"
        />
      </n-card>

      <n-card size="small" :bordered="false" :title="t('workflow.detail.timeline')" style="margin-top: 12px">
        <n-timeline v-if="detail.hisTasks?.length">
          <n-timeline-item
            v-for="item in detail.hisTasks"
            :key="item.id"
            :type="timelineType(item)"
            :title="item.nodeName || item.nodeId"
            :time="formatTime(item.createTime)"
          >
            <div class="wf-rec">
              <div class="wf-rec-meta">
                <span v-if="operatorText(item)" class="wf-rec-who">{{ operatorText(item) }}</span>
                <n-tag size="small" :type="actionTagType(item.action)" :bordered="false">
                  {{ actionLabel(item.action) }}
                </n-tag>
              </div>
              <blockquote v-if="item.comment" class="wf-comment">{{ item.comment }}</blockquote>
              <div v-if="transferText(item)" class="wf-meta">
                {{ t('workflow.detail.transferTo', { name: transferText(item) }) }}
              </div>
            </div>
          </n-timeline-item>
        </n-timeline>
        <n-empty v-else :description="t('workflow.detail.noHistory')" size="small" />
      </n-card>
    </template>

    <n-modal
      v-model:show="actionShow"
      preset="card"
      :title="actionTitle"
      style="width: 440px"
      :mask-closable="!actionSubmitting"
    >
      <n-space vertical style="width: 100%">
        <UserSelect
          v-if="actionKind === 'transfer' || actionKind === 'delegate'"
          v-model:value="actionForm.toUserId"
          :placeholder="actionKind === 'delegate' ? t('workflow.detail.delegateUser') : t('workflow.detail.transferUser')"
        />
        <n-select
          v-if="actionKind === 'return' && returnPolicy === 'any'"
          v-model:value="actionForm.targetNodeId"
          :options="returnTargetOptions"
          :placeholder="t('workflow.detail.returnTarget')"
        />
        <n-input
          v-if="actionKind !== 'urge' && actionKind !== 'cancel' && actionKind !== 'resubmit'"
          v-model:value="actionForm.comment"
          type="textarea"
          :rows="3"
          :placeholder="t('workflow.detail.commentHint')"
        />
        <n-space justify="end">
          <n-button :disabled="actionSubmitting" @click="actionShow = false">{{ t('common.cancel') }}</n-button>
          <n-button type="primary" :loading="actionSubmitting" @click="submitAction">{{ t('common.confirm') }}</n-button>
        </n-space>
      </n-space>
    </n-modal>
  </DetailPage>
</template>

<style scoped>
.wf-vars {
  margin: 12px 0 0;
  padding: 10px 12px;
  border-radius: var(--radius-md);
  background: var(--color-fill);
  display: grid;
  gap: 6px;
}
.wf-var-row {
  display: grid;
  grid-template-columns: minmax(72px, 140px) 1fr;
  gap: 12px;
  font-size: var(--font-size-sm);
  line-height: var(--line-height-sm);
}
.wf-var-row dt {
  margin: 0;
  color: var(--color-text-tertiary);
}
.wf-var-row dd {
  margin: 0;
  color: var(--color-text-primary);
  word-break: break-all;
}
.wf-rec-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.wf-rec-who {
  font-size: var(--font-size-sm);
  color: var(--color-text-secondary);
}
.wf-comment {
  margin: 8px 0 0;
  padding: 8px 10px;
  border-left: 3px solid var(--color-border-strong);
  background: var(--color-fill);
  border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
  color: var(--color-text-secondary);
  font-size: var(--font-size-sm);
}
.wf-meta {
  margin-top: 6px;
  font-size: var(--font-size-xs);
  color: var(--color-text-tertiary);
}
.wf-replay {
  overflow: auto;
}
</style>
