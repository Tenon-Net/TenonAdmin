import { forwardRef, useEffect, useImperativeHandle, useRef, useState, type RefObject } from 'react'
import { App, Form, Input, Modal, Select } from 'antd'
import { useTranslation } from 'react-i18next'
import { UserSelect } from '@/components/UserSelect'
import { useConfirm } from '@/hooks/useConfirm'
import { wfInstanceApi, wfTaskApi } from '@/api/workflow'
import { findNode, flattenChain } from '@/workflow/model'
import { classifyOutcome, useRequestKey } from '@/workflow/useRequestKey'
import { normalizeWfId, wfIdEquals, type WfId } from '@/workflow/id'
import type { WfModel } from '@/workflow/schema'
import type { WfCurrentTask, WfInstanceDetail, WfTodoItem } from '@/types/workflow'
import { WfFormMount, type WfFormMountHandle } from '../../components/WfFormMount'

export type WfTaskActionKind =
  | 'approve' | 'reject' | 'transfer' | 'return' | 'delegate'
  | 'addSign' | 'removeSign' | 'takeBack' | 'urge' | 'cancel' | 'resubmit'

const PENDING_ACTIONS: WfTaskActionKind[] = ['approve', 'reject', 'return', 'transfer', 'delegate', 'addSign', 'removeSign']
const USER_ACTIONS: WfTaskActionKind[] = ['transfer', 'delegate', 'addSign', 'removeSign']
const VARIABLE_ACTIONS: WfTaskActionKind[] = ['approve', 'reject', 'resubmit']
const NO_COMMENT_ACTIONS: WfTaskActionKind[] = ['urge', 'cancel', 'resubmit']

interface ActionFormValues {
  comment?: string
  toUserId?: WfId
  targetNodeId?: string
  selectedUserIdsByNode?: Record<string, WfId[] | undefined>
}

export interface WfTaskActionModalHandle {
  open: (kind: WfTaskActionKind) => void
}

interface WfTaskActionModalProps {
  instanceId: WfId | null
  detail: WfInstanceDetail | null
  replayModel: WfModel | null
  pendingTasks: WfTodoItem[]
  currentTasks: WfCurrentTask[]
  variablesJson: string | null
  formRuntimeRef: RefObject<WfFormMountHandle | null>
  onVariablesChange: (value: string | null) => void
  onCompleted: () => Promise<void>
}

export const WfTaskActionModal = forwardRef<WfTaskActionModalHandle, WfTaskActionModalProps>(
  function WfTaskActionModal({
    instanceId,
    detail,
    replayModel,
    pendingTasks,
    currentTasks,
    variablesJson,
    formRuntimeRef,
    onVariablesChange,
    onCompleted,
  }, ref) {
    const { t } = useTranslation()
    const { message } = App.useApp()
    const { run } = useConfirm()
    const requestKey = useRef(useRequestKey()).current
    const generationRef = useRef(0)
    const resubmitFormRuntimeRef = useRef<WfFormMountHandle | null>(null)
    const [form] = Form.useForm<ActionFormValues>()
    const [action, setAction] = useState<WfTaskActionKind | null>(null)
    const [submitting, setSubmitting] = useState(false)
    const [selectedPendingTaskId, setSelectedPendingTaskId] = useState<WfId | null>(null)
    const [urgeTaskId, setUrgeTaskId] = useState<WfId | null>(null)
    const pickedUserId = Form.useWatch('toUserId', form)

    const selectedPendingTask = pendingTasks.find((task) => wfIdEquals(task.taskId, selectedPendingTaskId))
    const currentNodeId = selectedPendingTask?.nodeId
      ?? (pendingTasks.length === 1 ? pendingTasks[0]?.nodeId : undefined)
      ?? (pendingTasks.length === 0 ? detail?.currentNodeIds?.[0] : undefined)
      ?? ''
    const currentNode = replayModel && currentNodeId ? findNode(replayModel.root, currentNodeId) : null
    const returnPolicy = currentNode?.props?.returnPolicy
    const returnTargetOptions = replayModel
      ? flattenChain(replayModel.root)
          .filter((node) => new Set(detail?.visitedNodeIds ?? []).has(node.id) && node.id !== currentNodeId)
          .map((node) => ({ label: node.name?.trim() || t(`workflow.node.${node.type}`), value: node.id }))
      : []
    const selfSelectNodes = replayModel
      ? flattenChain(replayModel.root).filter((node) => node.props?.assignee?.provider === 'selfSelect')
      : []
    const formSchema = replayModel?.formSchema ?? null
    const formMountPath = detail?.formComponent ?? detail?.model?.formComponent ?? null
    const blocksStarter = action === 'transfer' || action === 'delegate' || action === 'addSign'
    const starterDisabledIds = blocksStarter && detail?.starterUserId != null ? [detail.starterUserId] : []
    const starterPicked = blocksStarter
      && wfIdEquals(normalizeWfId(pickedUserId), normalizeWfId(detail?.starterUserId))

    // 变量是幂等载荷的一部分。网络失败后若表单内容改变,下一次提交必须使用新请求键。
    useEffect(() => { requestKey.reset() }, [variablesJson, requestKey])

    // 路由复用到另一实例时丢弃旧草稿,避免把旧弹窗里的动作提交到新实例。
    useEffect(() => {
      generationRef.current += 1
      setAction(null)
      setSubmitting(false)
      setSelectedPendingTaskId(null)
      setUrgeTaskId(null)
      form.resetFields()
      requestKey.reset()
    }, [instanceId, form, requestKey])

    // 后台刷新可能替换任务集合；失效的旧选择不能继续进入 API 载荷。
    useEffect(() => {
      if (action === null) return
      setSelectedPendingTaskId((current) => pendingTasks.some((task) => wfIdEquals(task.taskId, current))
        ? current
        : pendingTasks.length === 1 ? normalizeWfId(pendingTasks[0]!.taskId) : null)
      setUrgeTaskId((current) => currentTasks.some((task) => wfIdEquals(task.taskId, current))
        ? current
        : action === 'urge' ? normalizeWfId(currentTasks[0]?.taskId) : null)
      requestKey.reset()
    }, [action, currentTasks, pendingTasks, requestKey])

    useImperativeHandle(ref, () => ({
      open(kind) {
        form.resetFields()
        setSelectedPendingTaskId(pendingTasks.length === 1 ? normalizeWfId(pendingTasks[0]!.taskId) : null)
        setUrgeTaskId(kind === 'urge' ? normalizeWfId(currentTasks[0]?.taskId) : null)
        if (kind !== 'urge') requestKey.reset()
        setAction(kind)
      },
    }), [currentTasks, form, pendingTasks, requestKey])

    const taskIdFor = (kind: WfTaskActionKind): WfId | 0 => {
      if (kind === 'cancel' || kind === 'resubmit') return 0
      if (kind === 'urge') return urgeTaskId ?? 0
      if (kind === 'takeBack') return normalizeWfId(detail?.myTakeBackTaskId) ?? 0
      return selectedPendingTask?.taskId ?? 0
    }

    const submit = async () => {
      const kind = action
      if (!kind || instanceId === null) return
      const values = form.getFieldsValue()
      const toUserId = normalizeWfId(values.toUserId)

      if (USER_ACTIONS.includes(kind) && toUserId === null) {
        message.warning(t(`workflow.detail.${kind}Required`))
        return
      }
      if (blocksStarter && wfIdEquals(toUserId, normalizeWfId(detail?.starterUserId))) {
        message.error(t('workflow.detail.issue.starterNotAllowed'))
        return
      }
      if (kind === 'return' && returnPolicy === 'any' && !values.targetNodeId) {
        message.warning(t('workflow.detail.returnTargetRequired'))
        return
      }
      if ((kind === 'reject' || kind === 'return') && !values.comment?.trim()) {
        message.warning(t(kind === 'return' ? 'workflow.detail.returnReasonHint' : 'workflow.detail.commentRequired'))
        return
      }
      if ((values.comment?.trim().length ?? 0) > 1000) {
        message.warning(t('workflow.detail.commentTooLong'))
        return
      }
      const runtimeRef = kind === 'resubmit' ? resubmitFormRuntimeRef : formRuntimeRef
      if (VARIABLE_ACTIONS.includes(kind) && runtimeRef.current?.validate() === false) return

      const taskId = taskIdFor(kind)
      if (kind !== 'cancel' && kind !== 'resubmit' && normalizeWfId(taskId) === null) {
        message.warning(t('workflow.detail.taskRequired'))
        return
      }

      setSubmitting(true)
      const generation = generationRef.current
      try {
        const requestId = kind === 'urge' ? undefined : requestKey.value()
        const body = {
          taskId,
          comment: values.comment?.trim() || null,
          toUserId: toUserId ?? 0,
          targetNodeId: values.targetNodeId ?? null,
          requestId,
          ...(VARIABLE_ACTIONS.includes(kind) ? { variablesJson } : {}),
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
          if (kind === 'cancel') return wfInstanceApi.cancel({ instanceId, requestId })
          const picked = selfSelectNodes
            .map((node) => [node.id, values.selectedUserIdsByNode?.[node.id] ?? []] as const)
            .filter(([, ids]) => ids.length > 0)
          return wfInstanceApi.resubmit({
            instanceId,
            variablesJson,
            selectedUserIdsByNode: Object.fromEntries(picked),
            requestId,
          })
        }
        const api = async (): Promise<unknown> => {
          if (kind === 'urge') return dispatch()
          try {
            const result = await dispatch()
            if (generation === generationRef.current) requestKey.settle('success')
            return result
          } catch (error) {
            if (generation === generationRef.current) requestKey.settle(classifyOutcome(error))
            throw error
          }
        }
        if (!await run(api, t('common.success'))) return
        if (generation !== generationRef.current) return
        setAction(null)
        await onCompleted()
      } finally {
        if (generation === generationRef.current) setSubmitting(false)
      }
    }

    const pendingTaskText = (task: WfTodoItem) =>
      `${task.nodeName || task.nodeId || t('workflow.detail.parallel.unknown')} · #${task.taskId}`
    const currentTaskText = (task: WfCurrentTask) =>
      `${task.nodeName || task.nodeId || t('workflow.detail.parallel.unknown')} · #${task.taskId} · ${t('workflow.detail.parallel.token')} #${task.tokenId}${task.nodeVisitId == null ? '' : ` · NodeVisit #${task.nodeVisitId}`}`

    return (
      <Modal
        open={action !== null}
        title={action ? t(`workflow.detail.${action}`) : undefined}
        width={480}
        confirmLoading={submitting}
        mask={{ closable: !submitting }}
        keyboard={!submitting}
        closable={!submitting}
        cancelButtonProps={{ disabled: submitting }}
        okText={t('common.confirm')}
        cancelText={t('common.cancel')}
        onOk={() => void submit()}
        onCancel={() => { if (!submitting) setAction(null) }}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" onValuesChange={() => requestKey.reset()}>
          {action && USER_ACTIONS.includes(action) ? (
            <Form.Item
              name="toUserId"
              label={t(`workflow.detail.${action}User`)}
              validateStatus={starterPicked ? 'error' : undefined}
              help={starterPicked ? t('workflow.detail.issue.starterNotAllowed') : undefined}
            >
              <UserSelect
                source="workflow"
                placeholder={t(`workflow.detail.${action}User`)}
                disabledIds={starterDisabledIds}
                disabledSuffix={t('workflow.detail.issue.starterNotAllowed')}
              />
            </Form.Item>
          ) : null}
          {action && PENDING_ACTIONS.includes(action) && pendingTasks.length > 1 ? (
            <Form.Item label={t('workflow.detail.targetTask')} required>
              <Select
                value={selectedPendingTaskId}
                placeholder={t('workflow.detail.targetTask')}
                options={pendingTasks.map((task) => ({ label: pendingTaskText(task), value: task.taskId }))}
                onChange={(next: WfId) => { requestKey.reset(); setSelectedPendingTaskId(next) }}
              />
            </Form.Item>
          ) : null}
          {action === 'urge' && currentTasks.length > 1 ? (
            <Form.Item label={t('workflow.detail.targetTask')} required>
              <Select
                value={urgeTaskId}
                placeholder={t('workflow.detail.targetTask')}
                options={currentTasks.map((task) => ({ label: currentTaskText(task), value: task.taskId }))}
                onChange={(next: WfId) => { requestKey.reset(); setUrgeTaskId(next) }}
              />
            </Form.Item>
          ) : null}
          {action === 'return' && returnPolicy === 'any' ? (
            <Form.Item name="targetNodeId" label={t('workflow.detail.returnTarget')} required>
              <Select placeholder={t('workflow.detail.returnTarget')} options={returnTargetOptions} />
            </Form.Item>
          ) : null}
          {action === 'resubmit' ? (
            <>
              {formMountPath || formSchema ? (
                <WfFormMount
                  ref={resubmitFormRuntimeRef}
                  formComponent={formMountPath}
                  formSchema={formSchema}
                  mode="start"
                  permissions={null}
                  definitionId={detail?.definitionId}
                  instanceId={detail?.id}
                  businessKey={detail?.businessKey}
                  variablesJson={variablesJson}
                  status={detail?.status}
                  onVariablesChange={(next) => { requestKey.reset(); onVariablesChange(next) }}
                />
              ) : null}
              {selfSelectNodes.map((node) => (
                <Form.Item key={node.id} name={['selectedUserIdsByNode', node.id]} label={node.name || t('workflow.start.selfSelect')}>
                  <UserSelect source="workflow" mode="multiple" placeholder={t('workflow.start.selfSelectHint')} />
                </Form.Item>
              ))}
            </>
          ) : null}
          {action && !NO_COMMENT_ACTIONS.includes(action) ? (
            <Form.Item
              name="comment"
              label={action === 'return'
                ? t('workflow.detail.returnReasonHint')
                : action === 'reject' ? t('workflow.detail.commentRequired') : t('workflow.detail.commentHint')}
              required={action === 'reject' || action === 'return'}
            >
              <Input.TextArea
                rows={3}
                maxLength={1000}
                showCount
                placeholder={action === 'return'
                  ? t('workflow.detail.returnReasonHint')
                  : action === 'reject' ? t('workflow.detail.commentRequired') : t('workflow.detail.commentHint')}
              />
            </Form.Item>
          ) : null}
        </Form>
      </Modal>
    )
  },
)
