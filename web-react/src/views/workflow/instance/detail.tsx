// 审批实例详情:摘要 + 流程图回放 + 并行分支 + 业务表单 + 审批记录 + 事件流 + 全套办理动词。
// 约定式路由:views/workflow/instance/detail.tsx → /workflow/instance/:id/detail。对应 Vue 侧 instance/detail.vue。
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  App,
  Button,
  Card,
  Descriptions,
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
} from 'antd'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { Can } from '@/components/Can'
import { DetailPage } from '@/components/DetailPage'
import { UserSelect } from '@/components/UserSelect'
import { useConfirm } from '@/hooks/useConfirm'
import { useTabsStore } from '@/stores/tabs'
import { useUserStore } from '@/stores/user'
import { wfInstanceApi, wfTaskApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import { mergeWfFormPermissions } from '@/workflow/formRuntime'
import { projectWfRuntimeModel } from '@/workflow/formSchema'
import { findNode, flattenChain } from '@/workflow/model'
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
}

interface ActionFormValues {
  comment?: string
  toUserId?: number
  targetNodeId?: string
}

export default function WfInstanceDetailPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const { id } = useParams<{ id: string }>()
  const removeTab = useTabsStore((s) => s.removeTab)
  const myUserId = useUserStore((s) => s.userInfo?.userId)
  const { run } = useConfirm()

  // useRequestKey 是工厂(键存在闭包里)不是 hook:useRef 固定住首个实例,否则每次渲染都换一把新键。
  const requestKey = useRef(useRequestKey()).current

  const uid = Number(id)
  const [loading, setLoading] = useState(false)
  const [detail, setDetail] = useState<WfInstanceDetail | null>(null)
  const [history, setHistory] = useState<WfHistoryItem[]>([])
  const [formVariablesJson, setFormVariablesJson] = useState<string | null>(null)
  const [selectedPendingTaskId, setSelectedPendingTaskId] = useState<number | null>(null)
  const [urgeTaskId, setUrgeTaskId] = useState<number | null>(null)
  const [action, setAction] = useState<ActionKind | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [actionForm] = Form.useForm<ActionFormValues>()
  const formRuntimeRef = useRef<WfFormMountHandle | null>(null)
  // uid 连换或动作后重载时,只认最后一次请求的结果。
  const seqRef = useRef(0)

  const load = useCallback(async (instanceId: number) => {
    if (!Number.isFinite(instanceId) || instanceId <= 0) return
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
      setSelectedPendingTaskId(pending.length === 1 ? Number(pending[0]!.taskId) : null)
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

  const currentTasks = detail?.currentTasks ?? []
  const forks = detail?.parallelForks ?? []
  const hisTasks = useMemo(() => detail?.hisTasks ?? [], [detail])

  const selectedPendingTask = myPendingTasks.find((task) => Number(task.taskId) === selectedPendingTaskId)

  const isStarter = useMemo(() => {
    const starter = detail?.starterUserId
    if (starter == null) return false
    if (myUserId == null) return true
    return Number(starter) === Number(myUserId)
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
  const formMode = myPendingTasks.length > 0 || action === 'resubmit' ? 'approve' as const : 'view' as const

  // 字段权限:有待办时取各待办节点的 formPerms;否则回看我经手过的审批节点,按 hidden > readonly > editable 合并。
  const formPermissions = useMemo<WfFormFieldPerm[] | null>(() => {
    if (!replayModel) return null
    const nodeIds = myPendingTasks.length > 0
      ? myPendingTasks.map((task) => task.nodeId).filter((nodeId): nodeId is string => !!nodeId)
      : [
          ...(currentNode?.type === 'approval' ? [currentNode.id] : []),
          ...(detail?.visitedNodeIds ?? []),
          ...hisTasks
            .filter((task) => Number(task.userId) === Number(myUserId))
            .map((task) => task.nodeId)
            .filter((nodeId): nodeId is string => !!nodeId),
        ]
    const permissions = nodeIds
      .map((nodeId) => findNode(replayModel.root, nodeId))
      .filter((node) => node?.type === 'approval')
      .flatMap((node) => node?.props?.formPerms ?? [])
    const merged = mergeWfFormPermissions(permissions)
    return merged.length ? merged : null
  }, [replayModel, myPendingTasks, currentNode, detail, hisTasks, myUserId])

  const buttonLabels: WfButtonLabels = currentNode?.props?.buttonLabels ?? {}
  const returnPolicy = currentNode?.props?.returnPolicy

  const returnTargetOptions = useMemo(() => {
    if (!replayModel) return []
    const walked = new Set(detail?.visitedNodeIds ?? [])
    return flattenChain(replayModel.root)
      .filter((node) => walked.has(node.id) && node.id !== currentNodeId)
      .map((node) => ({ label: node.name?.trim() || t(`workflow.node.${node.type}`), value: node.id }))
  }, [replayModel, detail, currentNodeId, t])

  const canUrge = isStarter && isRunning && currentTasks.length > 0
  const canCancel = isStarter && isRunning && !hasApproveHistory
  const canResubmit = isStarter && isRunning && myPendingTasks.length === 0
  const canTakeBack = isRunning && Number(detail?.myTakeBackTaskId ?? 0) > 0 && Number(myUserId ?? 0) > 0

  const btnText = (kind: keyof WfButtonLabels, fallbackKey: string) =>
    buttonLabels[kind]?.trim() || t(fallbackKey)

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

  function openAction(kind: ActionKind) {
    actionForm.resetFields()
    setUrgeTaskId(kind === 'urge' ? Number(currentTasks[0]?.taskId ?? 0) || null : null)
    // 催办不进 receipt,不生成也不结算请求键;其余动作每次打开都换新键。
    if (kind !== 'urge') requestKey.reset()
    setAction(kind)
  }

  function taskIdFor(kind: ActionKind): number {
    if (kind === 'cancel' || kind === 'resubmit') return 0
    if (kind === 'urge') return Number(urgeTaskId ?? 0)
    if (kind === 'takeBack') return Number(detail?.myTakeBackTaskId ?? 0)
    return Number(selectedPendingTaskId ?? 0)
  }

  async function submitAction() {
    const kind = action
    if (!kind) return
    const values = actionForm.getFieldsValue()
    const toUserId = Number(values.toUserId ?? 0)

    if (USER_ACTIONS.includes(kind) && !(toUserId > 0)) {
      message.warning(t(`workflow.detail.${kind}Required`))
      return
    }
    if (kind === 'return' && returnPolicy === 'any' && !values.targetNodeId) {
      message.warning(t('workflow.detail.returnTargetRequired'))
      return
    }
    if (VARIABLE_ACTIONS.includes(kind) && formRuntimeRef.current?.validate() === false) return

    const taskId = taskIdFor(kind)
    if (kind !== 'cancel' && kind !== 'resubmit' && taskId <= 0) {
      message.warning(t('workflow.detail.taskRequired'))
      return
    }

    setSubmitting(true)
    try {
      const requestId = kind === 'urge' ? undefined : requestKey.value()
      const body = {
        taskId,
        comment: values.comment?.trim() || null,
        toUserId: toUserId || 0,
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
        if (kind === 'cancel') return wfInstanceApi.cancel({ instanceId: uid, requestId })
        return wfInstanceApi.resubmit({ instanceId: uid, variablesJson: formVariablesJson, requestId })
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
      if (await run(api, t('common.success'))) {
        setAction(null)
        await load(uid)
      }
    } finally {
      setSubmitting(false)
    }
  }

  const pendingTaskText = (task: WfTodoItem) =>
    `${task.nodeName || task.nodeId || t('workflow.detail.parallel.unknown')} · #${task.taskId}`

  const currentTaskText = (task: WfCurrentTask) =>
    `${task.nodeName || task.nodeId || t('workflow.detail.parallel.unknown')} · #${task.taskId} · ${t('workflow.detail.parallel.token')} #${task.tokenId}`

  const userFallback = (userId: number | string | null | undefined) =>
    userId == null || userId === '' ? '' : t('workflow.detail.userFallback', { id: userId })

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
                <Button onClick={() => openAction('transfer')}>{btnText('transfer', 'workflow.detail.transfer')}</Button>
                <Button onClick={() => openAction('delegate')}>{btnText('delegate', 'workflow.detail.delegate')}</Button>
                <Can code="POST:/api/v1/workflow/task/add-sign">
                  <Button onClick={() => openAction('addSign')}>{t('workflow.detail.addSign')}</Button>
                </Can>
                <Can code="POST:/api/v1/workflow/task/remove-sign">
                  <Button onClick={() => openAction('removeSign')}>{t('workflow.detail.removeSign')}</Button>
                </Can>
              </>
            ) : null}
            {canTakeBack ? (
              <Can code="POST:/api/v1/workflow/task/take-back">
                <Button onClick={() => openAction('takeBack')}>{t('workflow.detail.takeBack')}</Button>
              </Can>
            ) : null}
            {canUrge ? (
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
          <Card size="small" title={t('workflow.detail.summary')}>
            <Descriptions
              size="small" column={2}
              items={[
                {
                  key: 'definition', label: t('workflow.detail.definition'),
                  children: `${detail.definitionName ?? '—'}${detail.version ? ` (v${detail.version})` : ''}`,
                },
                {
                  key: 'status', label: t('common.status'),
                  children: (
                    <Tag color={instanceStatusColor(detail.status)}>
                      {t(`workflow.status.${normalizeInstanceStatus(detail.status)}`)}
                    </Tag>
                  ),
                },
                { key: 'businessKey', label: t('workflow.detail.businessKey'), children: detail.businessKey || '—' },
                { key: 'createTime', label: t('workflow.detail.createTime'), children: formatDateTime(detail.createTime) },
              ]}
            />
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
                  onModelChange={() => {}}
                  onSelect={() => {}}
                />
              </div>
              {currentTasks.length > 0 ? (
                <List
                  size="small"
                  dataSource={currentTasks}
                  rowKey={(task) => String(task.taskId)}
                  renderItem={(task) => <List.Item>{currentTaskText(task)}</List.Item>}
                />
              ) : null}
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
                ref={formRuntimeRef}
                formComponent={formMountPath}
                formSchema={formSchema}
                mode={formMode}
                permissions={formPermissions}
                definitionId={detail.definitionId == null ? undefined : Number(detail.definitionId)}
                instanceId={detail.id == null ? undefined : Number(detail.id)}
                businessKey={detail.businessKey}
                variablesJson={formVariablesJson}
                status={detail.status}
                onVariablesChange={setFormVariablesJson}
              />
            </Card>
          ) : null}

          <Card size="small" title={t('workflow.detail.timeline')}>
            {hisTasks.length > 0 ? (
              <Timeline
                items={hisTasks.map((item) => ({
                  color: TIMELINE_COLOR[taskActionColor(item.action)] ?? 'gray',
                  children: (
                    <Space orientation="vertical" size={4}>
                      <Space size={8} wrap>
                        <strong>{item.nodeName || item.nodeId}</strong>
                        {userFallback(item.userId) ? (
                          <Typography.Text type="secondary">{userFallback(item.userId)}</Typography.Text>
                        ) : null}
                        <Tag color={taskActionColor(item.action)}>
                          {t(`workflow.action.${normalizeTaskAction(item.action)}`)}
                        </Tag>
                        <Typography.Text type="secondary">{formatDateTime(item.createTime)}</Typography.Text>
                      </Space>
                      {item.comment ? <Typography.Text>{item.comment}</Typography.Text> : null}
                      {item.transferToUserId != null ? (
                        <Typography.Text type="secondary">
                          {t('workflow.detail.transferTo', { name: userFallback(item.transferToUserId) })}
                        </Typography.Text>
                      ) : null}
                      {item.targetUserId != null ? (
                        <Typography.Text type="secondary">
                          {t('workflow.detail.signTarget', { name: userFallback(item.targetUserId) })}
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

          <Card size="small" title={t('workflow.detail.eventTimeline')}>
            {history.length > 0 ? (
              <List
                size="small"
                dataSource={history}
                rowKey={(item) => String(item.id)}
                renderItem={(item) => (
                  <List.Item>
                    <Space size={8} wrap>
                      <span>{t(`workflow.detail.historyEvent.${HISTORY_EVENT[Number(item.eventType)] ?? 'unknown'}`, { type: item.eventType ?? '—' })}</span>
                      <Typography.Text type="secondary">
                        {`#${item.sequence || '—'} · ${t('workflow.detail.parallel.token')} #${item.tokenId ?? '—'} · NodeVisit #${item.nodeVisitId ?? '—'}${item.nodeId ? ` · ${item.nodeId}` : ''}`}
                      </Typography.Text>
                      <Typography.Text type="secondary">{formatDateTime(item.createTime)}</Typography.Text>
                    </Space>
                  </List.Item>
                )}
              />
            ) : (
              <Empty description={t('workflow.detail.noEventHistory')} />
            )}
          </Card>
        </Space>
      ) : null}

      <Modal
        open={action !== null}
        title={action ? t(`workflow.detail.${action}`) : undefined}
        width={480}
        confirmLoading={submitting}
        maskClosable={!submitting}
        okText={t('common.confirm')}
        cancelText={t('common.cancel')}
        onOk={() => void submitAction()}
        onCancel={() => setAction(null)}
        destroyOnHidden
      >
        <Form form={actionForm} layout="vertical">
          {action && USER_ACTIONS.includes(action) ? (
            <Form.Item name="toUserId" label={t(`workflow.detail.${action}User`)}>
              <UserSelect placeholder={t(`workflow.detail.${action}User`)} />
            </Form.Item>
          ) : null}

          {action && PENDING_ACTIONS.includes(action) && myPendingTasks.length > 1 ? (
            <Form.Item label={t('workflow.detail.targetTask')} required>
              <Select
                value={selectedPendingTaskId}
                placeholder={t('workflow.detail.targetTask')}
                options={myPendingTasks.map((task) => ({ label: pendingTaskText(task), value: Number(task.taskId) }))}
                onChange={setSelectedPendingTaskId}
              />
            </Form.Item>
          ) : null}

          {action === 'urge' && currentTasks.length > 1 ? (
            <Form.Item label={t('workflow.detail.targetTask')} required>
              <Select
                value={urgeTaskId}
                placeholder={t('workflow.detail.targetTask')}
                options={currentTasks.map((task) => ({ label: currentTaskText(task), value: Number(task.taskId) }))}
                onChange={setUrgeTaskId}
              />
            </Form.Item>
          ) : null}

          {action === 'return' && returnPolicy === 'any' ? (
            <Form.Item name="targetNodeId" label={t('workflow.detail.returnTarget')} required>
              <Select placeholder={t('workflow.detail.returnTarget')} options={returnTargetOptions} />
            </Form.Item>
          ) : null}

          {action && !NO_COMMENT_ACTIONS.includes(action) ? (
            <Form.Item name="comment" label={t('workflow.detail.commentHint')}>
              <Input.TextArea rows={3} placeholder={t('workflow.detail.commentHint')} />
            </Form.Item>
          ) : null}
        </Form>
      </Modal>
    </DetailPage>
  )
}
