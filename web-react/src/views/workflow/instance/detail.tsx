// 审批实例详情:摘要 + 流程图回放 + 并行分支 + 业务表单 + 审批记录 + 事件流 + 全套办理动词。
// 约定式路由:views/workflow/instance/detail.tsx → /workflow/instance/:id/detail。对应 Vue 侧 instance/detail.vue。
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  Alert,
  App,
  Button,
  Card,
  Collapse,
  Descriptions,
  Dropdown,
  Empty,
  Form,
  Input,
  List,
  Modal,
  Select,
  Space,
  Tag,
  Timeline,
  Typography,
  type MenuProps,
} from 'antd'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { Can } from '@/components/Can'
import { DetailPage } from '@/components/DetailPage'
import { UserSelect } from '@/components/UserSelect'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { useTabsStore } from '@/stores/tabs'
import { useUserStore } from '@/stores/user'
import { wfInstanceApi, wfTaskApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import { mergeWfFormPermissions, observerHiddenFields } from '@/workflow/formRuntime'
import { projectWfRuntimeModel } from '@/workflow/formSchema'
import { normalizeWfId, wfIdEquals, type WfId } from '@/workflow/id'
import { findNode, flattenChain } from '@/workflow/model'
import { runtimeStatusKey } from '@/workflow/runtimeStatus'
import {
  formatDateTime,
  instanceStatusColor,
  normalizeInstanceStatus,
  normalizeTaskAction,
  taskActionColor,
} from '@/workflow/statusLabels'
import { classifyOutcome, useRequestKey } from '@/workflow/useRequestKey'
import type { WfButtonLabels, WfFormFieldPerm } from '@/workflow/schema'
import type { WfCurrentTask, WfHistoryItem, WfInstanceDetail, WfTodoItem } from '@/types/workflow'
import { WfFormMount, type WfFormMountHandle } from '../components/WfFormMount'
import { WfNodeTree } from '../definition/components/WfNodeTree'
import { CurrentApprovalSummary, WfApprovalDetailModal } from './WfApprovalProgress'
import { buildApprovalProgress, type WfApprovalNodeProgress } from './approvalProgress'
import './approval-progress.css'

type ActionKind =
  | 'approve' | 'reject' | 'transfer' | 'return' | 'delegate'
  | 'addSign' | 'removeSign' | 'takeBack' | 'urge' | 'cancel' | 'resubmit'

/** 这些动作打在「我的某一条待办」上,多臂时必须先定下目标 task。 */
const PENDING_ACTIONS: ActionKind[] = ['approve', 'reject', 'return', 'transfer', 'delegate', 'addSign', 'removeSign']
/** 需要选人的动作。 */
const USER_ACTIONS: ActionKind[] = ['transfer', 'delegate', 'addSign', 'removeSign']
/** 会把表单变量一起提交的动作(其余动作不碰变量,免得把只读回放写回去)。 */
const VARIABLE_ACTIONS: ActionKind[] = ['approve', 'reject', 'resubmit']
/** 不需要意见框的动作。 */
const NO_COMMENT_ACTIONS: ActionKind[] = ['urge', 'cancel', 'resubmit']

/** 并行分叉状态 1 等待汇合 / 2 已汇合 / 3 已取消。 */
const FORK_STATUS: Record<number, { color: string; key: string }> = {
  1: { color: 'processing', key: 'waiting' },
  2: { color: 'success', key: 'joined' },
  3: { color: 'warning', key: 'cancelled' },
}

/** 并行臂状态 1 进行中 / 2 已完成 / 3 已取消。 */
const ARM_STATUS: Record<number, { color: string; key: string }> = {
  1: { color: 'processing', key: 'active' },
  2: { color: 'success', key: 'completed' },
  3: { color: 'warning', key: 'cancelled' },
}

/** Tag 语义色 → Timeline 节点色(Timeline 只认自己那套色名)。 */
const TIMELINE_COLOR: Record<string, string> = { success: 'green', error: 'red', warning: 'orange', processing: 'blue' }

/** 事件流 eventType → 文案键(与 Vue detail 同一张表)。 */
const HISTORY_EVENT: Record<number, string> = {
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
  21: 'nobodyHandled',
  22: 'adminForceTransferred',
  23: 'adminSkipped',
}

interface ActionFormValues {
  comment?: string
  toUserId?: WfId
  targetNodeId?: string
  selectedUserIdsByNode?: Record<string, WfId[] | undefined>
}

interface ResubmitEligibility {
  isStarter: boolean
  isRunning: boolean
  currentTasks: readonly unknown[]
  pendingTasks: readonly unknown[]
  history: readonly Pick<WfHistoryItem, 'eventType' | 'sequence' | 'createTime' | 'id'>[]
  forks: readonly { status?: number | string | null }[]
}

/** 只有最近一次重提之后又发生过退回,且实例确实停在可重提态时才允许发起人重提。 */
export function canResubmitInstance({
  isStarter, isRunning, currentTasks, pendingTasks, history, forks,
}: ResubmitEligibility): boolean {
  if (!isStarter || !isRunning || currentTasks.length > 0 || pendingTasks.length > 0) return false
  if (forks.some((fork) => Number(fork.status) === 1)) return false
  const lifecycle = history
    .filter((item) => Number(item.eventType) === 12 || Number(item.eventType) === 14)
    .sort((a, b) =>
      Number(a.sequence ?? 0) - Number(b.sequence ?? 0)
      || String(a.createTime ?? '').localeCompare(String(b.createTime ?? ''))
      || String(a.id ?? '').localeCompare(String(b.id ?? '')))
  return Number(lifecycle.at(-1)?.eventType) === 14
}

export default function WfInstanceDetailPage() {
  const { t, i18n } = useTranslation()
  const hasPerm = useHasPerm()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const { id } = useParams<{ id: string }>()
  const removeTab = useTabsStore((s) => s.removeTab)
  const myUserId = useUserStore((s) => s.userInfo?.userId)
  const { run } = useConfirm()

  // useRequestKey 是工厂(键存在闭包里)不是 hook:useRef 固定住首个实例,否则每次渲染都换一把新键。
  const requestKey = useRef(useRequestKey()).current

  const uid = normalizeWfId(id)
  const [loading, setLoading] = useState(false)
  const [detail, setDetail] = useState<WfInstanceDetail | null>(null)
  const [history, setHistory] = useState<WfHistoryItem[]>([])
  const [formVariablesJson, setFormVariablesJson] = useState<string | null>(null)
  const [selectedPendingTaskId, setSelectedPendingTaskId] = useState<WfId | null>(null)
  const [urgeTaskId, setUrgeTaskId] = useState<WfId | null>(null)
  const [action, setAction] = useState<ActionKind | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [inspectedProgress, setInspectedProgress] = useState<WfApprovalNodeProgress | null>(null)
  const [actionForm] = Form.useForm<ActionFormValues>()
  const pickedUserId = Form.useWatch('toUserId', actionForm)
  const formRuntimeRef = useRef<WfFormMountHandle | null>(null)
  const resubmitFormRuntimeRef = useRef<WfFormMountHandle | null>(null)
  // uid 连换或动作后重载时,只认最后一次请求的结果。
  const seqRef = useRef(0)

  const load = useCallback(async (instanceId: WfId | null) => {
    if (instanceId === null) return
    const seq = ++seqRef.current
    setLoading(true)
    try {
      const [loaded, loadedHistory] = await Promise.all([
        wfInstanceApi.get(instanceId),
        wfInstanceApi.history(instanceId),
      ])
      if (seq !== seqRef.current) return
      setDetail(loaded)
      setHistory(loadedHistory)
      setFormVariablesJson(loaded.variablesJson ?? null)
      const pending = loaded.myPendingTasks?.length
        ? loaded.myPendingTasks
        : loaded.myPendingTask ? [loaded.myPendingTask] : []
      // 只有一条待办才自动锁定目标;多条(并行多臂)留空,强制用户显式选一条,不做实例级兜底。
      setSelectedPendingTaskId(pending.length === 1 ? normalizeWfId(pending[0]!.taskId) : null)
    } catch (e) {
      if (seq !== seqRef.current) return
      message.error(translateError(e))
      setDetail(null)
      setHistory([])
    } finally {
      if (seq === seqRef.current) setLoading(false)
    }
  }, [message])

  useEffect(() => { void load(uid) }, [uid, load])

  // 路由态详情:返回 = 关掉本详情标签回待办列表(对齐 Vue detail 的 onBack)。
  const onBack = useCallback(() => {
    removeTab(pathname, pathname)
    navigate('/workflow/todo')
  }, [removeTab, pathname, navigate])

  const replayModel = useMemo(() => projectWfRuntimeModel(detail?.model), [detail])

  const myPendingTasks = useMemo<WfTodoItem[]>(() => {
    if (detail?.myPendingTasks?.length) return detail.myPendingTasks
    return detail?.myPendingTask ? [detail.myPendingTask] : []
  }, [detail])

  const currentTasks = useMemo(() => detail?.currentTasks ?? [], [detail])
  const pastTasks = useMemo(() => detail?.pastTasks ?? [], [detail])
  const forks = detail?.parallelForks ?? []
  const hisTasks = useMemo(() => detail?.hisTasks ?? [], [detail])
  const approvalProgress = useMemo(
    () => buildApprovalProgress(currentTasks, pastTasks, hisTasks),
    [currentTasks, pastTasks, hisTasks],
  )

  const selectedPendingTask = myPendingTasks.find((task) => wfIdEquals(task.taskId, selectedPendingTaskId))

  const isStarter = useMemo(() => {
    const starter = detail?.starterUserId
    if (starter == null) return false
    if (myUserId == null) return true
    return String(starter) === String(myUserId)
  }, [detail, myUserId])

  const isRunning = Number(detail?.status) === 1
  const hasApproveHistory = hisTasks.some((item) => Number(item.action) === 1)

  // 多 Token:未选目标 task 时不用 currentNodeIds[0] 兜底,否则按钮文案/退回策略会串到另一臂。
  const currentNodeId =
    selectedPendingTask?.nodeId
    ?? (myPendingTasks.length === 1 ? myPendingTasks[0]?.nodeId : undefined)
    ?? (myPendingTasks.length === 0 ? detail?.currentNodeIds?.[0] : undefined)
    ?? ''
  const currentNode = useMemo(
    () => (replayModel && currentNodeId ? findNode(replayModel.root, currentNodeId) : null),
    [replayModel, currentNodeId],
  )

  const formSchema = replayModel?.formSchema ?? null
  const formMountPath = detail?.formComponent ?? detail?.model?.formComponent ?? null
  const formMode = myPendingTasks.length > 0 ? 'approve' as const : 'view' as const

  // 办理人只看自己节点的字段权限。没有待办也没有审批历史的人按观察者处理:不是每个节点都隐藏的字段保持可见。
  const formPermissions = useMemo<WfFormFieldPerm[] | null>(() => {
    if (!replayModel) return null
    const ownNodeIds = myPendingTasks.length > 0
      ? myPendingTasks.map((task) => task.nodeId).filter((nodeId): nodeId is string => !!nodeId)
      : hisTasks
          .filter((task) => task.userId != null && myUserId != null && String(task.userId) === String(myUserId))
          .map((task) => task.nodeId)
          .filter((nodeId): nodeId is string => !!nodeId)
    if (ownNodeIds.length > 0) {
      const permissions = ownNodeIds
        .map((nodeId) => findNode(replayModel.root, nodeId))
        .filter((node) => node?.type === 'approval')
        .flatMap((node) => node?.props?.formPerms ?? [])
      const merged = mergeWfFormPermissions(permissions)
      return merged.length ? merged : null
    }
    const currentIds = detail?.currentNodeIds ?? []
    const candidateIds = currentIds.length > 0 ? currentIds : (detail?.visitedNodeIds ?? [])
    const hidden = observerHiddenFields(
      candidateIds.map((nodeId) => findNode(replayModel.root, nodeId)),
      replayModel.formSchema?.fields.map((field) => field.key) ?? [],
    )
    return hidden.length ? hidden : null
  }, [replayModel, myPendingTasks, detail, hisTasks, myUserId])

  const buttonLabels: WfButtonLabels = currentNode?.props?.buttonLabels ?? {}
  const returnPolicy = currentNode?.props?.returnPolicy

  const returnTargetOptions = useMemo(() => {
    if (!replayModel) return []
    const walked = new Set(detail?.visitedNodeIds ?? [])
    return flattenChain(replayModel.root)
      .filter((node) => walked.has(node.id) && node.id !== currentNodeId)
      .map((node) => ({ label: node.name?.trim() || t(`workflow.node.${node.type}`), value: node.id }))
  }, [replayModel, detail, currentNodeId, t])

  const selfSelectNodes = useMemo(
    () => (replayModel?.root
      ? flattenChain(replayModel.root).filter((node) => node.props?.assignee?.provider === 'selfSelect')
      : []),
    [replayModel],
  )

  const canUrge = isStarter && isRunning && currentTasks.length > 0
  const canCancel = isStarter && isRunning && !hasApproveHistory
  const canResubmit = canResubmitInstance({
    isStarter,
    isRunning,
    currentTasks,
    pendingTasks: myPendingTasks,
    history,
    forks,
  })
  const canTakeBack = isRunning
    && normalizeWfId(detail?.myTakeBackTaskId) !== null
    && normalizeWfId(myUserId) !== null

  const btnText = (kind: keyof WfButtonLabels, fallbackKey: string) =>
    buttonLabels[kind]?.trim() || t(fallbackKey)

  // 同意、拒绝、退回留在外面;转办、委托、加签、减签和催办收进「更多」。
  const moreItems: NonNullable<MenuProps['items']> = []
  if (myPendingTasks.length > 0) {
    moreItems.push(
      { key: 'transfer', label: btnText('transfer', 'workflow.detail.transfer') },
      { key: 'delegate', label: btnText('delegate', 'workflow.detail.delegate') },
    )
    if (hasPerm('POST:/api/v1/workflow/task/add-sign')) {
      moreItems.push({ key: 'addSign', label: t('workflow.detail.addSign') })
    }
    if (hasPerm('POST:/api/v1/workflow/task/remove-sign')) {
      moreItems.push({ key: 'removeSign', label: t('workflow.detail.removeSign') })
    }
    if (canUrge) moreItems.push({ key: 'urge', label: btnText('urge', 'workflow.detail.urge') })
  }

  const variableRows = useMemo(() => {
    const json = formVariablesJson
    if (!json?.trim()) return []
    try {
      const parsed: unknown = JSON.parse(json)
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
  }, [formVariablesJson])

  const blockStarter = action === 'transfer' || action === 'delegate' || action === 'addSign'
  const starterDisabledIds = blockStarter && detail?.starterUserId != null ? [detail.starterUserId] : []
  const starterPicked = blockStarter && wfIdEquals(normalizeWfId(pickedUserId), normalizeWfId(detail?.starterUserId))

  function openAction(kind: ActionKind) {
    actionForm.resetFields()
    setUrgeTaskId(kind === 'urge' ? normalizeWfId(currentTasks[0]?.taskId) : null)
    // 催办不进 receipt,不生成也不结算请求键;其余动作每次打开都换新键。
    if (kind !== 'urge') requestKey.reset()
    setAction(kind)
  }

  function taskIdFor(kind: ActionKind): WfId | 0 {
    if (kind === 'cancel' || kind === 'resubmit') return 0
    if (kind === 'urge') return urgeTaskId ?? 0
    if (kind === 'takeBack') return normalizeWfId(detail?.myTakeBackTaskId) ?? 0
    return selectedPendingTaskId ?? 0
  }

  async function submitAction() {
    const kind = action
    if (!kind) return
    const values = actionForm.getFieldsValue()
    const toUserId = normalizeWfId(values.toUserId)

    if (USER_ACTIONS.includes(kind) && toUserId === null) {
      message.warning(t(`workflow.detail.${kind}Required`))
      return
    }
    if ((kind === 'transfer' || kind === 'delegate' || kind === 'addSign') && wfIdEquals(toUserId, normalizeWfId(detail?.starterUserId))) {
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
    try {
      const requestId = kind === 'urge' ? undefined : requestKey.value()
      const body = {
        taskId,
        comment: values.comment?.trim() || null,
        toUserId: toUserId ?? 0,
        targetNodeId: values.targetNodeId ?? null,
        requestId,
        ...(VARIABLE_ACTIONS.includes(kind) ? { variablesJson: formVariablesJson } : {}),
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
        if (kind === 'cancel') return wfInstanceApi.cancel({ instanceId: uid!, requestId })
        const picked = selfSelectNodes
          .map((node) => [node.id, values.selectedUserIdsByNode?.[node.id] ?? []] as const)
          .filter(([, ids]) => ids.length > 0)
        return wfInstanceApi.resubmit({
          instanceId: uid!,
          variablesJson: formVariablesJson,
          selectedUserIdsByNode: Object.fromEntries(picked),
          requestId,
        })
      }
      // urge 不进 receipt(语义契约既定),不生成/不结算 key;其余动作按结果 settle 复用/丢弃 requestKey。
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
      if (!ok) return
      setAction(null)
      if (uid) await load(uid)
    } finally {
      setSubmitting(false)
    }
  }

  const pendingTaskText = (task: WfTodoItem) =>
    `${task.nodeName || task.nodeId || t('workflow.detail.parallel.unknown')} · #${task.taskId}`

  const currentTaskText = (task: WfCurrentTask) =>
    `${task.nodeName || task.nodeId || t('workflow.detail.parallel.unknown')} · #${task.taskId} · ${t('workflow.detail.parallel.token')} #${task.tokenId}${task.nodeVisitId == null ? '' : ` · NodeVisit #${task.nodeVisitId}`}`

  const nodeTitle = (nodeId?: string | null, nodeName?: string | null) => {
    const named = nodeName?.trim()
    if (named) return named
    const fromModel = nodeId && replayModel ? findNode(replayModel.root, nodeId)?.name?.trim() : ''
    return fromModel || t('workflow.detail.parallel.unknown')
  }

  const userFallback = (userId: number | string | null | undefined) =>
    userId == null || userId === '' ? '' : t('workflow.detail.userFallback', { id: userId })
  const operatorName = (name: string | null | undefined, userId: number | string | null | undefined) =>
    name?.trim() || (Number(userId) > 0 ? userFallback(userId) : t('workflow.detail.approvalProgress.system'))
  const nobodySummary = (item: { nodeName?: string | null; nodeId?: string | null; comment?: string | null; transferToUserName?: string | null; transferToUserId?: number | string | null }) => {
    const [action, reason] = String(item.comment ?? '').split('|')
    const node = item.nodeName || item.nodeId || ''
    const reasonKey = `workflow.detail.nobody.reason.${reason}`
    const reasonText = reason && i18n.exists(reasonKey) ? t(reasonKey) : t('workflow.detail.nobody.reason.noResolvedUsers')
    const target = item.transferToUserName || (item.transferToUserId != null ? userFallback(item.transferToUserId) : '') || t('workflow.detail.approvalProgress.system')
    const actionText = action === 'autoPass'
      ? t('workflow.detail.nobody.action.autoPass')
      : action === 'transfer'
        ? t('workflow.detail.nobody.action.transfer', { name: target })
        : t('workflow.detail.nobody.action.block')
    return t('workflow.detail.nobody.summary', { node, reason: reasonText, action: actionText })
  }

  const hasActions = myPendingTasks.length > 0 || canUrge || canCancel || canResubmit || canTakeBack

  return (
    <DetailPage
      title={detail?.definitionName || t('workflow.detail.title')}
      loading={loading}
      onBack={onBack}
      actions={
        detail && hasActions ? (
          <Space size={8} wrap>
            {myPendingTasks.length > 0 ? (
              <>
                <Button type="primary" onClick={() => openAction('approve')}>
                  {btnText('approve', 'workflow.detail.approve')}
                </Button>
                <Button danger onClick={() => openAction('reject')}>
                  {btnText('reject', 'workflow.detail.reject')}
                </Button>
                <Button onClick={() => openAction('return')}>{btnText('return', 'workflow.detail.return')}</Button>
                {moreItems.length > 0 ? (
                  <Dropdown
                    menu={{ items: moreItems, onClick: ({ key }) => openAction(key as ActionKind) }}
                    trigger={['click']}
                  >
                    <Button>{t('common.more')}</Button>
                  </Dropdown>
                ) : null}
              </>
            ) : null}
            {canTakeBack ? (
              <Can code="POST:/api/v1/workflow/task/take-back">
                <Button onClick={() => openAction('takeBack')}>{t('workflow.detail.takeBack')}</Button>
              </Can>
            ) : null}
            {canUrge && myPendingTasks.length === 0 ? (
              <Button onClick={() => openAction('urge')}>{btnText('urge', 'workflow.detail.urge')}</Button>
            ) : null}
            {canCancel ? <Button onClick={() => openAction('cancel')}>{t('workflow.detail.cancel')}</Button> : null}
            {canResubmit ? (
              <Button type="primary" onClick={() => openAction('resubmit')}>{t('workflow.detail.resubmit')}</Button>
            ) : null}
          </Space>
        ) : null
      }
    >
      {detail ? (
        <Space orientation="vertical" size={12} style={{ width: '100%' }}>
          {detail.returnedToStarter ? (
            <Alert type="warning" showIcon title={t('workflow.detail.returnedHint')} />
          ) : null}
          {(detail.blockedNodeIds?.length ?? 0) > 0 ? (
            <Alert
              type="warning"
              showIcon
              title={t('workflow.detail.blockedHint', {
                name: (detail.blockedNodeIds ?? [])
                  .map((id) => (replayModel ? findNode(replayModel.root, id)?.name : '') || id)
                  .join('、'),
              })}
            />
          ) : null}
          <Card size="small" title={t('workflow.detail.summary')}>
            <Descriptions
              size="small" column={2}
              items={[
                {
                  key: 'definition', label: t('workflow.detail.definition'),
                  children: `${detail.definitionName ?? '—'}${detail.version ? ` (v${detail.version})` : ''}`,
                },
                {
                  key: 'starter', label: t('workflow.detail.starter'),
                  children: detail.starterName?.trim()
                    || (detail.starterUserId != null && detail.starterUserId !== ''
                      ? t('workflow.detail.userFallback', { id: detail.starterUserId })
                      : '—'),
                },
                {
                  key: 'status', label: t('common.status'),
                  children: (
                    <Tag color={runtimeStatusKey(detail) ? 'gold' : instanceStatusColor(detail.status)}>
                      {t(`workflow.status.${runtimeStatusKey(detail) ?? normalizeInstanceStatus(detail.status)}`)}
                    </Tag>
                  ),
                },
                { key: 'businessKey', label: t('workflow.detail.businessKey'), children: detail.businessKey || '—' },
                { key: 'createTime', label: t('workflow.detail.createTime'), children: formatDateTime(detail.createTime) },
              ]}
            />
            <CurrentApprovalSummary tasks={currentTasks} />
            {variableRows.length > 0 && !formSchema ? (
              <Descriptions
                size="small" column={1} style={{ marginTop: 12 }}
                items={variableRows.map((row, index) => ({
                  key: row.key || String(index),
                  label: row.key || t('workflow.detail.variables'),
                  children: row.value,
                }))}
              />
            ) : null}
          </Card>

          {replayModel ? (
            <Card size="small" title={t('workflow.detail.replay')}>
              <div style={{ overflow: 'auto' }}>
                <WfNodeTree
                  model={replayModel}
                  readonly
                  visitedIds={detail.visitedNodeIds ?? []}
                  currentIds={detail.currentNodeIds ?? []}
                  runtimeProgress={approvalProgress}
                  onInspectApproval={setInspectedProgress}
                  onModelChange={() => {}}
                  onSelect={() => {}}
                />
              </div>
            </Card>
          ) : null}

          {forks.length > 0 && (
            <Card size="small" title={t('workflow.detail.parallel.title')}>
              <Space orientation="vertical" size={12} style={{ width: '100%' }}>
                {forks.map((fork) => {
                  const forkStatus = FORK_STATUS[Number(fork.status)]
                  return (
                    <div key={String(fork.forkId)}>
                      <Space size={8} wrap>
                        <strong>{fork.nodeName || fork.nodeId || `#${fork.forkId}`}</strong>
                        <Tag color={forkStatus?.color ?? 'default'}>
                          {t(`workflow.detail.parallel.${forkStatus?.key ?? 'unknown'}`)}
                        </Tag>
                        <Typography.Text type="secondary">
                          {t('workflow.detail.parallel.join', { count: fork.pendingArmCount ?? 0 })}
                        </Typography.Text>
                      </Space>
                      <List
                        size="small"
                        dataSource={fork.arms ?? []}
                        rowKey={(arm) => `${fork.forkId}-${arm.armId}`}
                        renderItem={(arm) => {
                          const armStatus = ARM_STATUS[Number(arm.status)]
                          return (
                            <List.Item>
                              <Space size={8} wrap>
                                <span>{arm.armId}</span>
                                <Tag color={armStatus?.color ?? 'default'}>
                                  {t(`workflow.detail.parallel.${armStatus?.key ?? 'unknown'}`)}
                                </Tag>
                                <Typography.Text type="secondary">
                                  {arm.currentNodeId
                                    ? `${arm.currentNodeName || arm.currentNodeId} · ${t('workflow.detail.parallel.token')} #${arm.childTokenId}`
                                    : t('workflow.detail.parallel.emptyArm')}
                                </Typography.Text>
                                {arm.reason ? <Typography.Text type="secondary">{arm.reason}</Typography.Text> : null}
                              </Space>
                            </List.Item>
                          )
                        }}
                      />
                    </div>
                  )
                })}
              </Space>
            </Card>
          )}

          {formMountPath || formSchema ? (
            <Card size="small" title={t('workflow.detail.form')}>
              <WfFormMount
                ref={action === 'resubmit' ? undefined : formRuntimeRef}
                formComponent={formMountPath}
                formSchema={formSchema}
                mode={formMode}
                permissions={formPermissions}
                definitionId={detail.definitionId}
                instanceId={detail.id}
                businessKey={detail.businessKey}
                variablesJson={formVariablesJson}
                status={detail.status}
                onVariablesChange={(next) => {
                  requestKey.reset()
                  setFormVariablesJson(next)
                }}
              />
            </Card>
          ) : null}

          <Card size="small" title={t('workflow.detail.timeline')}>
            {hisTasks.length > 0 ? (
              <Timeline
                items={hisTasks.map((item) => ({
                  color: TIMELINE_COLOR[taskActionColor(item.action)] ?? 'gray',
                  content: (
                    <Space orientation="vertical" size={4}>
                      <Space size={8} wrap>
                        <strong>{nodeTitle(item.nodeId, item.nodeName)}</strong>
                        <Typography.Text type="secondary">
                          {item.isAutomatic
                            ? t('workflow.detail.approvalProgress.automatic')
                            : operatorName(item.operatorName, item.userId)}
                        </Typography.Text>
                        {item.isAutomatic && Number(item.userId) > 0 ? (
                          <Typography.Text type="secondary">
                            {t('workflow.detail.approvalProgress.onBehalfOf', { name: operatorName(item.operatorName, item.userId) })}
                          </Typography.Text>
                        ) : null}
                        <Tag color={taskActionColor(item.action)}>
                          {t(`workflow.action.${normalizeTaskAction(item.action)}`)}
                        </Tag>
                        <Typography.Text type="secondary">{formatDateTime(item.createTime)}</Typography.Text>
                      </Space>
                      {Number(item.action) === 11 ? (
                        <Typography.Text>{nobodySummary(item)}</Typography.Text>
                      ) : item.comment ? <Typography.Text>{item.comment}</Typography.Text> : null}
                      {Number(item.action) !== 11 && item.transferToUserId != null ? (
                        <Typography.Text type="secondary">
                          {t('workflow.detail.transferTo', { name: item.transferToUserName || userFallback(item.transferToUserId) })}
                        </Typography.Text>
                      ) : null}
                      {item.targetUserId != null ? (
                        <Typography.Text type="secondary">
                          {t('workflow.detail.signTarget', { name: item.targetUserName || userFallback(item.targetUserId) })}
                        </Typography.Text>
                      ) : null}
                    </Space>
                  ),
                }))}
              />
            ) : (
              <Empty description={t('workflow.detail.noHistory')} />
            )}
          </Card>

          <Collapse
            items={[{
              key: 'eventTimeline',
              label: t('workflow.detail.eventTimeline'),
              children: history.length > 0 ? (
                <List
                  size="small"
                  dataSource={history}
                  rowKey={(item) => String(item.id)}
                  renderItem={(item) => (
                    <List.Item>
                      <Space size={8} wrap>
                        <span>{t(`workflow.detail.historyEvent.${HISTORY_EVENT[Number(item.eventType)] ?? 'unknown'}`, { type: item.eventType ?? '—' })}</span>
                        <Typography.Text type="secondary">
                          {`#${item.sequence || '—'} · ${t('workflow.detail.parallel.token')} #${item.tokenId ?? '—'} · NodeVisit #${item.nodeVisitId ?? '—'}${item.nodeId ? ` · ${nodeTitle(item.nodeId)}` : ''}`}
                        </Typography.Text>
                        <Typography.Text type="secondary">{formatDateTime(item.createTime)}</Typography.Text>
                      </Space>
                    </List.Item>
                  )}
                />
              ) : <Empty description={t('workflow.detail.noEventHistory')} />,
            }]}
          />
        </Space>
      ) : null}

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
        onOk={() => void submitAction()}
        onCancel={() => { if (!submitting) setAction(null) }}
        destroyOnHidden
      >
        <Form form={actionForm} layout="vertical" onValuesChange={() => requestKey.reset()}>
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

          {action && PENDING_ACTIONS.includes(action) && myPendingTasks.length > 1 ? (
            <Form.Item label={t('workflow.detail.targetTask')} required>
              <Select
                value={selectedPendingTaskId}
                placeholder={t('workflow.detail.targetTask')}
                options={myPendingTasks.map((task) => ({ label: pendingTaskText(task), value: task.taskId }))}
                onChange={(next: WfId) => {
                  requestKey.reset()
                  setSelectedPendingTaskId(next)
                }}
              />
            </Form.Item>
          ) : null}

          {action === 'urge' && currentTasks.length > 1 ? (
            <Form.Item label={t('workflow.detail.targetTask')} required>
              <Select
                value={urgeTaskId}
                placeholder={t('workflow.detail.targetTask')}
                options={currentTasks.map((task) => ({ label: currentTaskText(task), value: task.taskId }))}
                onChange={(next: WfId) => {
                  requestKey.reset()
                  setUrgeTaskId(next)
                }}
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
              {(formMountPath || formSchema) ? (
                <WfFormMount
                  ref={resubmitFormRuntimeRef}
                  formComponent={formMountPath}
                  formSchema={formSchema}
                  mode="start"
                  permissions={null}
                  definitionId={detail?.definitionId}
                  instanceId={detail?.id}
                  businessKey={detail?.businessKey}
                  variablesJson={formVariablesJson}
                  status={detail?.status}
                  onVariablesChange={(next) => {
                    requestKey.reset()
                    setFormVariablesJson(next)
                  }}
                />
              ) : null}
              {selfSelectNodes.map((node) => (
                <Form.Item
                  key={node.id}
                  name={['selectedUserIdsByNode', node.id]}
                  label={node.name || t('workflow.start.selfSelect')}
                >
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
                : action === 'reject'
                  ? t('workflow.detail.commentRequired')
                  : t('workflow.detail.commentHint')}
              required={action === 'reject' || action === 'return'}
            >
              <Input.TextArea
                rows={3}
                maxLength={1000}
                showCount
                placeholder={action === 'return'
                  ? t('workflow.detail.returnReasonHint')
                  : action === 'reject'
                    ? t('workflow.detail.commentRequired')
                    : t('workflow.detail.commentHint')}
              />
            </Form.Item>
          ) : null}
        </Form>
      </Modal>
      <WfApprovalDetailModal
        progress={inspectedProgress}
        open={inspectedProgress !== null}
        onClose={() => setInspectedProgress(null)}
      />
    </DetailPage>
  )
}
