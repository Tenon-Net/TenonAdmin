<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, reactive, shallowRef, useTemplateRef, watch } from 'vue'
import {
  NAlert,
  NButton,
  NFormItem,
  NInput,
  NModal,
  NSelect,
  NSpace,
  useMessage,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import UserSelect from '@/components/UserSelect/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { wfInstanceApi, wfTaskApi } from '@/api/workflow'
import type { WfCurrentTask, WfInstanceDetail, WfTodoItem } from '@/types/workflow'
import type { WfFormSchema, WfReturnPolicy } from '@/workflow/schema'
import { projectWfRuntimeModel } from '@/workflow/formSchema'
import { findNode, flattenChain } from '@/workflow/model'
import { ACTOR_STATUS } from '@/workflow/approvalProgress'
import { classifyOutcome, useRequestKey } from '@/workflow/useRequestKey'
import { normalizeWfId, wfIdEquals, type WfId } from '@/workflow/id'
import WfFormMount from '../../components/WfFormMount.vue'
import type { WfInstanceActionKind } from './actionTypes'

const {
  instanceId,
  detail,
  pendingTasks,
  currentTasks,
  formComponent = null,
  formSchema = null,
  variablesJson = null,
  validateCurrentForm,
} = defineProps<{
  instanceId: WfId
  detail: WfInstanceDetail
  pendingTasks: readonly WfTodoItem[]
  currentTasks: readonly WfCurrentTask[]
  formComponent?: string | null
  formSchema?: WfFormSchema | null
  variablesJson?: string | null
  validateCurrentForm?: () => boolean
}>()

const emit = defineEmits<{ completed: [] }>()
const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const requestKey = useRequestKey()

const show = shallowRef(false)
const kind = shallowRef<WfInstanceActionKind>('approve')
const submitting = shallowRef(false)
const selectedTaskId = shallowRef<WfId | null>(null)
const targetTaskId = shallowRef<WfId | null>(null)
const resubmitVariablesJson = shallowRef<string | null>(null)
const actionForm = reactive({
  comment: '',
  toUserId: null as WfId | null,
  targetNodeId: null as string | null,
})
const resubmitForm = useTemplateRef<{ validate: () => boolean }>('resubmitForm')

const pendingActionKinds: readonly WfInstanceActionKind[] = [
  'approve', 'reject', 'return', 'transfer', 'delegate', 'addSign', 'removeSign',
]
const requiresPendingTask = computed(() => pendingActionKinds.includes(kind.value))
const selectedTask = computed(() =>
  pendingTasks.find((task) => wfIdEquals(task.taskId, selectedTaskId.value)) ?? pendingTasks[0],
)
const currentNodeId = computed(() => selectedTask.value?.nodeId ?? detail.currentNodeIds?.[0] ?? '')
const currentNode = computed(() => {
  const model = projectWfRuntimeModel(detail.model)
  return model && currentNodeId.value ? findNode(model.root, currentNodeId.value) : null
})
const returnPolicy = computed<WfReturnPolicy | undefined>(() => currentNode.value?.props?.returnPolicy)
const returnTargetOptions = computed(() => {
  const model = projectWfRuntimeModel(detail.model)
  if (!model) return []
  const walked = new Set(detail.visitedNodeIds ?? [])
  return flattenChain(model.root)
    .filter((node) => walked.has(node.id) && node.id !== currentNodeId.value)
    .map((node) => ({
      label: node.name?.trim() || t(`workflow.node.${node.type}`),
      value: node.id,
    }))
})
const blockStarter = computed(() => ['transfer', 'delegate', 'addSign'].includes(kind.value))
const starterDisabledIds = computed(() =>
  blockStarter.value && detail.starterUserId != null ? [detail.starterUserId] : [],
)
const title = computed(() => t(`workflow.detail.${kind.value}`))
const commentRequired = computed(() => kind.value === 'reject' || kind.value === 'return')
const commentFieldLabel = computed(() => {
  if (kind.value === 'return') return t('workflow.detail.returnReason')
  if (kind.value === 'reject') return t('workflow.detail.rejectReason')
  return t('workflow.detail.commentLabel')
})
const commentPlaceholder = computed(() => {
  if (kind.value === 'return') return t('workflow.detail.returnReasonHint')
  if (kind.value === 'reject') return t('workflow.detail.commentRequired')
  return t('workflow.detail.commentHint')
})
const commentMaxLength = 1000

function sameUser(left: WfId | null | undefined, right: WfId | null | undefined) {
  if (left == null || right == null || left === '' || right === '') return false
  return wfIdEquals(left, right)
}

function resetDraft() {
  actionForm.comment = ''
  actionForm.toUserId = null
  actionForm.targetNodeId = null
}

function open(nextKind: WfInstanceActionKind) {
  if (submitting.value) return
  if (kind.value !== nextKind) resetDraft()
  kind.value = nextKind
  selectedTaskId.value = normalizeWfId(pendingTasks[0]?.taskId)
  targetTaskId.value = nextKind === 'urge' || nextKind === 'takeBack'
    ? nextKind === 'takeBack'
      ? normalizeWfId(detail.myTakeBackTaskId)
      : normalizeWfId(currentTasks[0]?.taskId)
    : normalizeWfId(selectedTask.value?.taskId)
  resubmitVariablesJson.value = variablesJson
  if (nextKind !== 'urge') requestKey.reset()
  show.value = true
}

defineExpose({ open })

watch(selectedTask, (task) => {
  if (show.value && requiresPendingTask.value) targetTaskId.value = normalizeWfId(task?.taskId)
})

let generation = 0
function invalidateInstance() {
  generation++
  show.value = false
  submitting.value = false
  requestKey.reset()
  resetDraft()
}
watch(() => instanceId, invalidateInstance, { flush: 'sync' })
onBeforeUnmount(invalidateInstance)
// 缓存页切走时收起传送到 body 的弹层；同一实例的在途提交仍可完成并通知页面刷新。
onDeactivated(() => { show.value = false })

function pendingTaskOption(task: WfTodoItem) {
  const value = normalizeWfId(task.taskId)
  return value == null ? [] : [{ label: task.nodeName || task.nodeId || t('workflow.detail.parallel.unknown'), value }]
}

function currentTaskOption(task: WfCurrentTask) {
  const value = normalizeWfId(task.taskId)
  const node = task.nodeName || task.nodeId || t('workflow.detail.parallel.unknown')
  const pending = (task.participants ?? [])
    .filter((person) => Number(person.status) === ACTOR_STATUS.pending)
    .map((person) => person.displayName?.trim() || t('workflow.detail.userFallback', { id: person.userId }))
  return value == null ? [] : [{ label: pending.length ? `${node}：${pending.join('、')}` : node, value }]
}

function onCommentKeydown(event: KeyboardEvent) {
  if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
    event.preventDefault()
    void submit()
  }
}

function validateDraft(): boolean {
  if ((kind.value === 'transfer' || kind.value === 'delegate') && normalizeWfId(actionForm.toUserId) == null) {
    message.warning(t(kind.value === 'delegate' ? 'workflow.detail.delegateRequired' : 'workflow.detail.transferRequired'))
    return false
  }
  if ((kind.value === 'addSign' || kind.value === 'removeSign') && normalizeWfId(actionForm.toUserId) == null) {
    message.warning(t(kind.value === 'addSign' ? 'workflow.detail.addSignRequired' : 'workflow.detail.removeSignRequired'))
    return false
  }
  if (blockStarter.value && sameUser(actionForm.toUserId, detail.starterUserId)) {
    message.error(t('workflow.detail.issue.starterNotAllowed'))
    return false
  }
  if (kind.value === 'return' && returnPolicy.value === 'any' && !actionForm.targetNodeId) {
    message.warning(t('workflow.detail.returnTargetRequired'))
    return false
  }
  if (commentRequired.value && !actionForm.comment.trim()) {
    message.warning(t(kind.value === 'return' ? 'workflow.detail.returnReasonHint' : 'workflow.detail.commentRequired'))
    return false
  }
  if (actionForm.comment.trim().length > commentMaxLength) {
    message.warning(t('workflow.detail.commentTooLong'))
    return false
  }
  const valid = kind.value === 'resubmit' ? resubmitForm.value?.validate() : validateCurrentForm?.()
  if ((kind.value === 'approve' || kind.value === 'reject' || kind.value === 'resubmit') && valid === false) {
    message.warning(t('workflow.detail.formInvalid'))
    return false
  }
  return true
}

function dispatch(requestId: string | undefined, taskId: WfId | null): Promise<unknown> {
  const body = {
    taskId: taskId ?? 0,
    comment: actionForm.comment.trim() || null,
    toUserId: actionForm.toUserId ?? 0,
    targetNodeId: actionForm.targetNodeId,
    requestId,
    ...((kind.value === 'approve' || kind.value === 'reject') ? { variablesJson } : {}),
  }
  if (kind.value === 'approve') return wfTaskApi.approve(body)
  if (kind.value === 'reject') return wfTaskApi.reject(body)
  if (kind.value === 'transfer') return wfTaskApi.transfer(body)
  if (kind.value === 'return') return wfTaskApi.return(body)
  if (kind.value === 'delegate') return wfTaskApi.delegate(body)
  if (kind.value === 'addSign') return wfTaskApi.addSign(body)
  if (kind.value === 'removeSign') return wfTaskApi.removeSign(body)
  if (kind.value === 'takeBack') return wfTaskApi.takeBack(body)
  if (kind.value === 'urge') return wfTaskApi.urge({ taskId: taskId! })
  if (kind.value === 'cancel') return wfInstanceApi.cancel({ instanceId, requestId })
  return wfInstanceApi.resubmit({ instanceId, variablesJson: resubmitVariablesJson.value, requestId })
}

async function submit() {
  if (submitting.value || !validateDraft()) return
  const requestGeneration = generation
  submitting.value = true
  try {
    const taskId = normalizeWfId(targetTaskId.value)
    if (!['cancel', 'resubmit'].includes(kind.value) && taskId == null) {
      message.warning(t('workflow.detail.taskRequired'))
      return
    }
    const requestId = kind.value === 'urge' ? undefined : requestKey.value()
    const action = async (): Promise<unknown> => {
      if (kind.value === 'urge') return dispatch(requestId, taskId)
      try {
        const result = await dispatch(requestId, taskId)
        if (requestGeneration === generation) requestKey.settle('success')
        return result
      } catch (error) {
        if (requestGeneration === generation) requestKey.settle(classifyOutcome(error))
        throw error
      }
    }
    const ok = await run(action, t('common.success'))
    if (!ok || requestGeneration !== generation) return
    show.value = false
    resetDraft()
    emit('completed')
  } finally {
    if (requestGeneration === generation) submitting.value = false
  }
}
</script>

<template>
  <n-modal
    v-model:show="show"
    preset="card"
    :title="title"
    style="width: 440px"
    :closable="!submitting"
    :close-on-esc="!submitting"
    :mask-closable="!submitting"
  >
    <n-space vertical style="width: 100%">
      <UserSelect
        v-if="kind === 'transfer' || kind === 'delegate' || kind === 'addSign' || kind === 'removeSign'"
        v-model:value="actionForm.toUserId"
        source="workflow"
        :disabled="submitting"
        :disabled-ids="starterDisabledIds"
        :disabled-suffix="t('workflow.detail.issue.starterNotAllowed')"
        :placeholder="kind === 'delegate'
          ? t('workflow.detail.delegateUser')
          : kind === 'addSign'
            ? t('workflow.detail.addSignUser')
            : kind === 'removeSign'
              ? t('workflow.detail.removeSignUser')
              : t('workflow.detail.transferUser')"
      />
      <n-alert v-if="blockStarter && sameUser(actionForm.toUserId, detail.starterUserId)" type="warning" :show-icon="false">
        {{ t('workflow.detail.issue.starterNotAllowed') }}
      </n-alert>
      <n-select
        v-if="requiresPendingTask && pendingTasks.length > 1"
        v-model:value="selectedTaskId"
        :disabled="submitting"
        :options="pendingTasks.flatMap(pendingTaskOption)"
        :placeholder="t('workflow.detail.targetTask')"
      />
      <n-select
        v-if="kind === 'urge' && currentTasks.length > 1"
        v-model:value="targetTaskId"
        :disabled="submitting"
        :options="currentTasks.flatMap(currentTaskOption)"
        :placeholder="t('workflow.detail.targetTask')"
      />
      <n-select
        v-if="kind === 'return' && returnPolicy === 'any'"
        v-model:value="actionForm.targetNodeId"
        :disabled="submitting"
        :options="returnTargetOptions"
        :placeholder="t('workflow.detail.returnTarget')"
      />
      <WfFormMount
        v-if="kind === 'resubmit' && (formComponent || formSchema)"
        ref="resubmitForm"
        :form-component="formComponent"
        :form-schema="formSchema"
        mode="start"
        :permissions="null"
        :definition-id="detail.definitionId ?? undefined"
        :instance-id="detail.id ?? undefined"
        :business-key="detail.businessKey"
        :variables-json="resubmitVariablesJson"
        :status="detail.status"
        @variables-change="resubmitVariablesJson = $event"
      />
      <n-form-item
        v-if="kind !== 'urge' && kind !== 'cancel' && kind !== 'resubmit'"
        :label="commentFieldLabel"
        :required="commentRequired"
        :show-feedback="false"
      >
        <n-input
          v-model:value="actionForm.comment"
          type="textarea"
          :rows="3"
          :maxlength="commentMaxLength"
          show-count
          :disabled="submitting"
          :placeholder="commentPlaceholder"
          @keydown="onCommentKeydown"
        />
      </n-form-item>
      <n-space justify="end">
        <n-button :disabled="submitting" @click="show = false">{{ t('common.cancel') }}</n-button>
        <n-button type="primary" :loading="submitting" :disabled="submitting" @click="submit">{{ t('common.confirm') }}</n-button>
      </n-space>
    </n-space>
  </n-modal>
</template>
