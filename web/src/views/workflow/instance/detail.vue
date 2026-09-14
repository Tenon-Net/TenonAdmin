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
import type {
  WfCurrentTask,
  WfHistoryItem,
  WfHisTask,
  WfInstanceDetail,
  WfInstanceStatus,
  WfParallelArm,
  WfParallelFork,
  WfTaskAction,
  WfTodoItem,
} from '@/types/workflow'
import type { WfButtonLabels, WfFormFieldPerm, WfFormSchema, WfReturnPolicy } from '@/workflow/schema'
import { projectWfRuntimeModel } from '@/workflow/formSchema'
import { findNode, flattenChain } from '@/workflow/model'
import { mergeWfFormPermissions } from '@/workflow/formRuntime'
import { classifyOutcome, useRequestKey } from '@/workflow/useRequestKey'
import WfFormMount from '../components/WfFormMount.vue'
import WfNodeTree from '../definition/components/WfNodeTree.vue'

type ActionKind = 'approve' | 'reject' | 'transfer' | 'return' | 'delegate' | 'addSign' | 'removeSign' | 'takeBack' | 'urge' | 'cancel' | 'resubmit'

const props = defineProps<{ id?: number | string }>()
const emit = defineEmits<{ back: [] }>()

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const message = useMessage()
const setTabTitle = useTabTitle()
const tabs = useTabsStore()
const { run } = useConfirm()
const requestKey = useRequestKey()

const isInline = computed(() => props.id != null)
const uid = computed(() => Number(props.id ?? route.params.id))

const user = useUserStore()
const loading = ref(false)
const detail = ref<WfInstanceDetail | null>(null)
const history = ref<WfHistoryItem[]>([])
const formVariablesJson = ref<string | null>(null)
const formRuntimeRef = ref<{ validate: () => boolean } | null>(null)
const targetTaskId = ref<number | null>(null)
const selectedPendingTaskId = ref<number | null>(null)

const pendingActionKinds: ActionKind[] = ['approve', 'reject', 'return', 'transfer', 'delegate', 'addSign', 'removeSign']

const replayModel = computed(() => projectWfRuntimeModel(detail.value?.model))

const visitedIds = computed(() => detail.value?.visitedNodeIds ?? [])
const currentIds = computed(() => detail.value?.currentNodeIds ?? [])
const currentTasks = computed<WfCurrentTask[]>(() => detail.value?.currentTasks ?? [])
const myPendingTasks = computed<WfTodoItem[]>(() => {
  if (detail.value?.myPendingTasks?.length) return detail.value.myPendingTasks as WfTodoItem[]
  return detail.value?.myPendingTask ? [detail.value.myPendingTask as WfTodoItem] : []
})
const selectedPendingTask = computed(() =>
  myPendingTasks.value.find((task) => Number(task.taskId) === selectedPendingTaskId.value)
  ?? myPendingTasks.value[0],
)
const requiresPendingTask = computed(() => pendingActionKinds.includes(actionKind.value))

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
  selectedPendingTask.value?.nodeId
  ?? detail.value?.currentNodeIds?.[0]
  ?? '',
)

const currentNode = computed(() => {
  const model = replayModel.value
  if (!model || !currentNodeId.value) return null
  return findNode(model.root, currentNodeId.value)
})

const formSchema = computed<WfFormSchema | null>(() => {
  return replayModel.value?.formSchema ?? null
})

const formMountPath = computed(() => detail.value?.formComponent ?? detail.value?.model?.formComponent ?? null)

const formMode = computed(() => selectedPendingTask.value || actionKind.value === 'resubmit' ? 'approve' as const : 'view' as const)

const formPermissions = computed<WfFormFieldPerm[] | null>(() => {
  const model = replayModel.value
  if (!model) return null
  const nodeIds = myPendingTasks.value.length > 0
    ? myPendingTasks.value.map((task) => task.nodeId).filter((id): id is string => Boolean(id))
    : [
        ...(currentNode.value?.type === 'approval' ? [currentNode.value.id] : []),
        ...(detail.value?.visitedNodeIds ?? []),
        ...(detail.value?.hisTasks ?? [])
          .filter((task) => Number(task.userId) === Number(user.userInfo?.userId))
          .map((task) => task.nodeId)
          .filter((id): id is string => Boolean(id)),
      ]
  const permissions = nodeIds
    .map((id) => findNode(model.root, id))
    .filter((node) => node?.type === 'approval')
    .flatMap((node) => node?.props?.formPerms ?? [])
  const merged = mergeWfFormPermissions(permissions)
  return merged.length ? merged : null
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
  isStarter.value && isRunning.value && currentTasks.value.length > 0,
)

const canCancel = computed(() => isStarter.value && isRunning.value && !hasApproveHistory.value)

const canResubmit = computed(() =>
  isStarter.value && isRunning.value && myPendingTasks.value.length === 0,
)

const canTakeBack = computed(() => {
  const me = Number(user.userInfo?.userId ?? 0)
  return isRunning.value
    && Number(detail.value?.myTakeBackTaskId ?? 0) > 0
    && me > 0
})

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
    8: 'addSign',
    9: 'removeSign',
    10: 'takeBack',
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

function parallelForkStatusLabel(status: WfParallelFork['status']): string {
  const key: Record<number, string> = { 1: 'waiting', 2: 'joined', 3: 'cancelled' }
  return t(`workflow.detail.parallel.${key[Number(status)] ?? 'unknown'}`)
}

function parallelArmStatusLabel(status: WfParallelArm['status']): string {
  const key: Record<number, string> = { 1: 'active', 2: 'completed', 3: 'cancelled' }
  return t(`workflow.detail.parallel.${key[Number(status)] ?? 'unknown'}`)
}

function parallelForkTagType(status: WfParallelFork['status']): 'default' | 'success' | 'warning' | 'info' {
  if (Number(status) === 2) return 'success'
  if (Number(status) === 3) return 'warning'
  return 'info'
}

function parallelArmTagType(status: WfParallelArm['status']): 'default' | 'success' | 'warning' | 'info' {
  if (Number(status) === 2) return 'success'
  if (Number(status) === 3) return 'warning'
  return 'info'
}

function historyEventLabel(item: WfHistoryItem): string {
  const key: Record<number, string> = {
    1: 'instanceStarted',
    2: 'instanceCompleted',
    3: 'nodeEnter',
    4: 'nodeLeave',
    7: 'taskCreated',
    8: 'taskCompleted',
    12: 'resubmitted',
    13: 'rejectRouted',
    14: 'taskReturned',
    17: 'parallelFork',
    18: 'parallelArmCompleted',
    19: 'parallelJoined',
    20: 'parallelCancelled',
  }
  return t(`workflow.detail.historyEvent.${key[Number(item.eventType)] ?? 'unknown'}`, {
    type: item.eventType ?? '—',
  })
}

function currentTaskText(task: WfCurrentTask) {
  return `${task.nodeName || task.nodeId || t('workflow.detail.parallel.unknown')} · #${task.taskId} · ${t('workflow.detail.parallel.token')} #${task.tokenId}`
}

function pendingTaskText(task: WfTodoItem) {
  return `${task.nodeName || task.nodeId || t('workflow.detail.parallel.unknown')} · #${task.taskId}`
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

function signTargetText(item: WfHisTask) {
  if (item.targetUserId == null) return ''
  return userFallback(item.targetUserId)
}

type VarPair = { key: string; value: string }

const variableRows = computed((): VarPair[] => {
  const json = formVariablesJson.value
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
    const [loaded, loadedHistory] = await Promise.all([
      wfInstanceApi.get(id),
      wfInstanceApi.history(id),
    ])
    detail.value = loaded
    history.value = loadedHistory
    selectedPendingTaskId.value = myPendingTasks.value[0]?.taskId == null
      ? null
      : Number(myPendingTasks.value[0].taskId)
    formVariablesJson.value = detail.value.variablesJson ?? null
    if (!isInline.value) setTabTitle(detail.value.definitionName ?? '')
  } catch (e) {
    message.error(translateError(e))
    detail.value = null
    history.value = []
  } finally {
    loading.value = false
  }
}

watch(selectedPendingTask, (task) => {
  formVariablesJson.value = detail.value?.variablesJson ?? null
  if (actionShow.value && requiresPendingTask.value)
    targetTaskId.value = task?.taskId == null ? null : Number(task.taskId)
})

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
  targetTaskId.value = kind === 'urge' || kind === 'takeBack'
    ? kind === 'takeBack'
      ? Number(detail.value?.myTakeBackTaskId ?? 0) || null
      : Number(currentTasks.value[0]?.taskId ?? 0) || null
    : Number(selectedPendingTask.value?.taskId ?? 0) || null
  if (kind !== 'urge') requestKey.reset()
  actionShow.value = true
}

const actionTitle = computed(() => t(`workflow.detail.${actionKind.value}`))

async function submitAction() {
  const kind = actionKind.value
  if ((kind === 'transfer' || kind === 'delegate') && (!actionForm.toUserId || actionForm.toUserId <= 0)) {
    message.warning(t(kind === 'delegate' ? 'workflow.detail.delegateRequired' : 'workflow.detail.transferRequired'))
    return
  }
  if ((kind === 'addSign' || kind === 'removeSign') && (!actionForm.toUserId || actionForm.toUserId <= 0)) {
    message.warning(t(kind === 'addSign' ? 'workflow.detail.addSignRequired' : 'workflow.detail.removeSignRequired'))
    return
  }
  if (kind === 'return' && returnPolicy.value === 'any' && !actionForm.targetNodeId) {
    message.warning(t('workflow.detail.returnTargetRequired'))
    return
  }
  if ((kind === 'approve' || kind === 'reject' || kind === 'resubmit') && formRuntimeRef.value?.validate() === false) return

  actionSubmitting.value = true
  try {
    const taskId = Number(targetTaskId.value ?? 0)
    if (!['cancel', 'resubmit'].includes(kind) && taskId <= 0) {
      message.warning(t('workflow.detail.taskRequired'))
      return
    }
    const requestId = kind === 'urge' ? undefined : requestKey.value()
    const body = {
      taskId,
      comment: actionForm.comment.trim() || null,
      toUserId: actionForm.toUserId ?? 0,
      targetNodeId: actionForm.targetNodeId,
      requestId,
      ...((kind === 'approve' || kind === 'reject' || kind === 'resubmit')
        ? { variablesJson: formVariablesJson.value }
        : {}),
    }
    const dispatch = (): Promise<unknown> => {
      if (kind === 'approve') return wfTaskApi.approve(body)
      if (kind === 'reject') return wfTaskApi.reject(body)
      if (kind === 'transfer') return wfTaskApi.transfer(body)
      if (kind === 'return') return wfTaskApi.return(body)
      if (kind === 'delegate') return wfTaskApi.delegate(body)
      if (kind === 'addSign') return wfTaskApi.addSign(body)
      if (kind === 'removeSign') return wfTaskApi.removeSign(body)
      if (kind === 'takeBack') return wfTaskApi.takeBack(body)
      if (kind === 'urge') return wfTaskApi.urge({ taskId })
      if (kind === 'cancel') return wfInstanceApi.cancel({ instanceId: uid.value, requestId })
      return wfInstanceApi.resubmit({
        instanceId: uid.value,
        variablesJson: formVariablesJson.value,
        requestId,
      })
    }
    // urge 不进 receipt(语义契约既定),不生成/不结算 key,其余动作按结果 settle 复用/丢弃 requestKey。
    const api = async (): Promise<unknown> => {
      if (kind === 'urge') return dispatch()
      try {
        const res = await dispatch()
        requestKey.settle('success')
        return res
      } catch (e) {
        requestKey.settle(classifyOutcome(e))
        throw e
      }
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
    <template v-if="detail && (myPendingTasks.length || canUrge || canCancel || canResubmit || canTakeBack)" #actions>
      <n-space>
        <n-button v-if="myPendingTasks.length" type="primary" @click="openAction('approve')">
          {{ btnText('approve', 'workflow.detail.approve') }}
        </n-button>
        <n-button v-if="myPendingTasks.length" type="error" @click="openAction('reject')">
          {{ btnText('reject', 'workflow.detail.reject') }}
        </n-button>
        <n-button v-if="myPendingTasks.length" @click="openAction('return')">
          {{ btnText('return', 'workflow.detail.return') }}
        </n-button>
        <n-button v-if="myPendingTasks.length" @click="openAction('transfer')">
          {{ btnText('transfer', 'workflow.detail.transfer') }}
        </n-button>
        <n-button v-if="myPendingTasks.length" @click="openAction('delegate')">
          {{ btnText('delegate', 'workflow.detail.delegate') }}
        </n-button>
        <n-button v-if="myPendingTasks.length" v-auth="'POST:/api/v1/workflow/task/add-sign'" @click="openAction('addSign')">
          {{ t('workflow.detail.addSign') }}
        </n-button>
        <n-button v-if="myPendingTasks.length" v-auth="'POST:/api/v1/workflow/task/remove-sign'" @click="openAction('removeSign')">
          {{ t('workflow.detail.removeSign') }}
        </n-button>
        <n-button v-if="canTakeBack" v-auth="'POST:/api/v1/workflow/task/take-back'" @click="openAction('takeBack')">
          {{ t('workflow.detail.takeBack') }}
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
        <dl v-if="variableRows.length && !formSchema" class="wf-vars">
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
          <div v-if="currentTasks.length" class="wf-current-tasks">
            <div v-for="task in currentTasks" :key="task.taskId" class="wf-current-task">
              {{ currentTaskText(task) }}
            </div>
          </div>
        </div>
      </n-card>

      <n-card
        v-if="detail.parallelForks?.length"
        size="small"
        :bordered="false"
        :title="t('workflow.detail.parallel.title')"
        style="margin-top: 12px"
      >
        <div v-for="fork in detail.parallelForks" :key="fork.forkId" class="wf-fork">
          <div class="wf-fork-heading">
            <strong>{{ fork.nodeName || fork.nodeId || `#${fork.forkId}` }}</strong>
            <n-tag size="small" :type="parallelForkTagType(fork.status)" :bordered="false">
              {{ parallelForkStatusLabel(fork.status) }}
            </n-tag>
            <span class="wf-meta">
              {{ t('workflow.detail.parallel.join', { count: fork.pendingArmCount ?? 0 }) }}
            </span>
          </div>
          <div v-for="arm in fork.arms" :key="`${fork.forkId}-${arm.armId}`" class="wf-arm">
            <span class="wf-arm-name">{{ arm.armId }}</span>
            <n-tag size="small" :type="parallelArmTagType(arm.status)" :bordered="false">
              {{ parallelArmStatusLabel(arm.status) }}
            </n-tag>
            <span v-if="arm.currentNodeId" class="wf-meta">
              {{ arm.currentNodeName || arm.currentNodeId }} · {{ t('workflow.detail.parallel.token') }} #{{ arm.childTokenId }}
            </span>
            <span v-else class="wf-meta">{{ t('workflow.detail.parallel.emptyArm') }}</span>
            <span v-if="arm.reason" class="wf-meta">{{ arm.reason }}</span>
          </div>
        </div>
      </n-card>

      <n-card
        v-if="formMountPath || formSchema"
        size="small"
        :bordered="false"
        :title="t('workflow.detail.form')"
        style="margin-top: 12px"
      >
        <WfFormMount
          ref="formRuntimeRef"
          :form-component="formMountPath"
          :form-schema="formSchema"
          :mode="formMode"
          :permissions="formPermissions"
          :definition-id="detail.definitionId == null ? undefined : Number(detail.definitionId)"
          :instance-id="detail.id == null ? undefined : Number(detail.id)"
          :business-key="detail.businessKey"
          :variables-json="formVariablesJson"
          :status="detail.status"
          @variables-change="formVariablesJson = $event"
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
              <div v-if="signTargetText(item)" class="wf-meta">
                {{ t('workflow.detail.signTarget', { name: signTargetText(item) }) }}
              </div>
            </div>
          </n-timeline-item>
        </n-timeline>
        <n-empty v-else :description="t('workflow.detail.noHistory')" size="small" />
      </n-card>

      <n-card size="small" :bordered="false" :title="t('workflow.detail.eventTimeline')" style="margin-top: 12px">
        <n-timeline v-if="history.length">
          <n-timeline-item
            v-for="item in history"
            :key="item.id"
            :title="historyEventLabel(item)"
            :time="formatTime(item.createTime)"
          >
            <div class="wf-meta">
              #{{ item.sequence || '—' }} · {{ t('workflow.detail.parallel.token') }} #{{ item.tokenId ?? '—' }}
              · NodeVisit #{{ item.nodeVisitId ?? '—' }}
              <span v-if="item.nodeId"> · {{ item.nodeId }}</span>
            </div>
          </n-timeline-item>
        </n-timeline>
        <n-empty v-else :description="t('workflow.detail.noEventHistory')" size="small" />
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
          v-if="actionKind === 'transfer' || actionKind === 'delegate' || actionKind === 'addSign' || actionKind === 'removeSign'"
          v-model:value="actionForm.toUserId"
          :placeholder="actionKind === 'delegate'
            ? t('workflow.detail.delegateUser')
            : actionKind === 'addSign'
              ? t('workflow.detail.addSignUser')
              : actionKind === 'removeSign'
                ? t('workflow.detail.removeSignUser')
                : t('workflow.detail.transferUser')"
        />
        <n-select
          v-if="requiresPendingTask && myPendingTasks.length > 1"
          v-model:value="selectedPendingTaskId"
          :options="myPendingTasks.map((task) => ({ label: pendingTaskText(task), value: Number(task.taskId) }))"
          :placeholder="t('workflow.detail.targetTask')"
        />
        <n-select
          v-if="actionKind === 'urge' && currentTasks.length > 1"
          v-model:value="targetTaskId"
          :options="currentTasks.map((task) => ({ label: currentTaskText(task), value: Number(task.taskId) }))"
          :placeholder="t('workflow.detail.targetTask')"
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
.wf-current-tasks,
.wf-fork {
  display: grid;
  gap: 8px;
}
.wf-current-tasks {
  margin: 0 12px 12px;
}
.wf-current-task,
.wf-arm {
  padding: 8px 10px;
  border-radius: var(--radius-sm);
  background: var(--color-fill);
  font-size: var(--font-size-sm);
}
.wf-fork + .wf-fork {
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid var(--color-divider);
}
.wf-fork-heading,
.wf-arm {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.wf-arm-name {
  min-width: 96px;
  color: var(--color-text-primary);
}
</style>
