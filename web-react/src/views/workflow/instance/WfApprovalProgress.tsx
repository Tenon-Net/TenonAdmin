import { Collapse, Empty, Modal, Space, Tag, Typography } from 'antd'
import { useTranslation } from 'react-i18next'
import { formatDateTime, normalizeTaskAction, taskActionColor } from '@/workflow/statusLabels'
import type { WfCurrentTask } from '@/types/workflow'
import {
  countersignProgress,
  participantHistory,
  participantName,
  participantState,
  type WfApprovalNodeProgress,
  type WfApprovalRound,
} from './approvalProgress'

const PENDING_STATUS = 1

function userName(name: string | null | undefined, userId: number | string | null | undefined, fallback: string): string {
  return name?.trim() || (Number(userId) > 0 ? `#${userId}` : fallback)
}

function approvalModeKey(mode: WfCurrentTask['approvalMode']): string {
  return mode === 'any' || mode === 'all' || mode === 'seq' ? mode : 'unknown'
}

function RoundContent({ round }: { round: WfApprovalRound }) {
  const { t } = useTranslation()
  const participants = [...(round.task?.participants ?? [])]
    .sort((a, b) => Number(a.sort ?? 0) - Number(b.sort ?? 0))
  const attributedIds = new Set(
    participants
      .map((participant) => participantHistory(participant, round)?.id)
      .filter((id) => id != null)
      .map((id) => String(id)),
  )
  const detailHistory = round.history.filter((item) => {
    const shownOnAssignee = attributedIds.has(String(item.id)) && [1, 2].includes(Number(item.action))
    return !shownOnAssignee
  })
  const ratio = countersignProgress(round)

  return (
    <Space orientation="vertical" size={10} style={{ width: '100%' }}>
      {round.task?.approvalMode ? (
        <Typography.Text type="secondary">
          {t('workflow.detail.approvalProgress.modeLabel')}：
          {t(`workflow.detail.approvalProgress.mode.${approvalModeKey(round.task.approvalMode)}`)}
        </Typography.Text>
      ) : null}
      {ratio ? (
        <Typography.Text>{t('workflow.detail.approvalProgress.ratio', ratio)}</Typography.Text>
      ) : null}
      {participants.length > 0 ? (
        <ul className="wf-approval-list">
          {participants.map((participant) => {
            const state = participantState(participant, round)
            const action = state.action == null ? null : normalizeTaskAction(state.action)
            const handled = participantHistory(participant, round)
            const automatic = handled?.isAutomatic === true
            const original = participant.originalUserId == null
              ? null
              : userName(participant.originalDisplayName, participant.originalUserId, t('workflow.detail.approvalProgress.system'))
            return (
              <li key={String(participant.actorId)} className="wf-approval-person">
                <Space orientation="vertical" size={2} style={{ width: '100%' }}>
                  <Space size={8} wrap>
                    <strong>{automatic ? t('workflow.detail.approvalProgress.automatic') : participantName(participant)}</strong>
                    <Tag color={state.key === 'pending'
                      ? 'processing'
                      : action && state.action != null ? taskActionColor(state.action) : 'default'}>
                      {action
                        ? t(`workflow.action.${action}`)
                        : t(`workflow.detail.approvalProgress.status.${state.key}`)}
                    </Tag>
                    <Typography.Text type="secondary">
                      {formatDateTime(handled?.createTime ?? participant.activatedTime)}
                    </Typography.Text>
                  </Space>
                  {automatic ? (
                    <Typography.Text type="secondary">
                      {t('workflow.detail.approvalProgress.onBehalfOf', { name: participantName(participant) })}
                    </Typography.Text>
                  ) : null}
                  {original ? (
                    <Typography.Text type="secondary">
                      {t('workflow.detail.approvalProgress.originalOwner', { name: original })}
                    </Typography.Text>
                  ) : null}
                  {handled?.comment ? <Typography.Text>{handled.comment}</Typography.Text> : null}
                </Space>
              </li>
            )
          })}
        </ul>
      ) : null}

      {detailHistory.length > 0 ? (
        <ul className="wf-approval-list">
          {detailHistory.map((item) => {
            const operator = userName(item.operatorName, item.userId, t('workflow.detail.approvalProgress.system'))
            const automatic = item.isAutomatic === true
            const original = item.originalUserId == null
              ? null
              : userName(item.originalUserName, item.originalUserId, t('workflow.detail.approvalProgress.system'))
            const target = item.transferToUserId != null
              ? userName(item.transferToUserName, item.transferToUserId, t('workflow.detail.approvalProgress.system'))
              : item.targetUserId != null
                ? userName(item.targetUserName, item.targetUserId, t('workflow.detail.approvalProgress.system'))
                : null
            return (
              <li key={String(item.id)}>
                <Space orientation="vertical" size={2} style={{ width: '100%' }}>
                  <Space size={8} wrap>
                    <strong>{automatic ? t('workflow.detail.approvalProgress.automatic') : operator}</strong>
                    <Tag color={taskActionColor(item.action)}>{t(`workflow.action.${normalizeTaskAction(item.action)}`)}</Tag>
                    <Typography.Text type="secondary">{formatDateTime(item.createTime)}</Typography.Text>
                  </Space>
                  {automatic && operator !== t('workflow.detail.approvalProgress.system') ? (
                    <Typography.Text type="secondary">
                      {t('workflow.detail.approvalProgress.onBehalfOf', { name: operator })}
                    </Typography.Text>
                  ) : null}
                  {original ? <Typography.Text type="secondary">{t('workflow.detail.approvalProgress.originalOwner', { name: original })}</Typography.Text> : null}
                  {target ? <Typography.Text type="secondary">{t('workflow.detail.approvalProgress.targetUser', { name: target })}</Typography.Text> : null}
                  {item.comment ? <Typography.Text>{item.comment}</Typography.Text> : null}
                </Space>
              </li>
            )
          })}
        </ul>
      ) : null}

      {!participants.length && !round.history.length ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('workflow.detail.approvalProgress.noRoundData')} />
      ) : null}

      {(round.task || round.history.some((item) => item.taskId != null || item.tokenId != null || item.nodeVisitId != null)) ? (
        <Collapse
          ghost
          size="small"
          items={[{
            key: 'technical',
            label: t('workflow.detail.approvalProgress.technical'),
            children: (
              <Typography.Text type="secondary" copyable>
                {`Task #${round.task?.taskId ?? round.history[0]?.taskId ?? '—'} · Token #${round.task?.tokenId ?? round.history[0]?.tokenId ?? '—'} · NodeVisit #${round.task?.nodeVisitId ?? round.history[0]?.nodeVisitId ?? '—'}`}
              </Typography.Text>
            ),
          }]}
        />
      ) : null}
    </Space>
  )
}

export function WfApprovalDetailModal({
  progress,
  open,
  onClose,
}: {
  progress: WfApprovalNodeProgress | null
  open: boolean
  onClose: () => void
}) {
  const { t } = useTranslation()
  if (!progress) return null

  const past = [...progress.pastRounds].reverse()
  return (
    <Modal
      open={open}
      title={progress.nodeName || progress.nodeId}
      footer={null}
      width={640}
      onCancel={onClose}
      destroyOnHidden
    >
      <div data-wf-approval-detail={progress.nodeId}>
        <Space orientation="vertical" size={14} style={{ width: '100%' }}>
          {progress.currentRounds.map((round) => (
            <section key={round.key}>
              <Space size={8} style={{ marginBottom: 8 }}>
                <strong>{t('workflow.detail.approvalProgress.currentRound')}</strong>
                <Tag color="processing">{t('workflow.status.running')}</Tag>
              </Space>
              <RoundContent round={round} />
            </section>
          ))}
          {past.length > 0 ? (
            <Collapse
              items={[{
                key: 'past',
                label: t('workflow.detail.approvalProgress.pastRounds', { n: past.length }),
                children: (
                  <Collapse
                    items={past.map((round, index) => ({
                      key: round.key,
                      label: t('workflow.detail.approvalProgress.pastRound', { n: past.length - index }),
                      children: <RoundContent round={round} />,
                    }))}
                  />
                ),
              }]}
            />
          ) : null}
          {!progress.currentRounds.length && !past.length ? (
            <Empty description={t('workflow.detail.approvalProgress.noRoundData')} />
          ) : null}
        </Space>
      </div>
    </Modal>
  )
}

export function CurrentApprovalSummary({ tasks }: { tasks: readonly WfCurrentTask[] }) {
  const { t } = useTranslation()
  const approvalTasks = tasks.filter((task) => task.approvalMode != null || (task.participants?.length ?? 0) > 0)
  if (!approvalTasks.length) return null

  return (
    <div className="wf-current-approvals" data-wf-current-approvals>
      <Typography.Text strong>{t('workflow.detail.approvalProgress.currentWaiting')}</Typography.Text>
      <Space orientation="vertical" size={4} style={{ width: '100%' }}>
        {approvalTasks.map((task) => {
          const pending = (task.participants ?? []).filter((person) => Number(person.status) === PENDING_STATUS)
          const ratio = countersignProgress({ key: String(task.taskId), current: true, task, history: [] })
          return (
            <Space key={String(task.taskId)} size={8} wrap>
              <Tag color="processing">{task.nodeName || task.nodeId}</Tag>
              <span>{pending.length
                ? pending.map(participantName).join('、')
                : t('workflow.detail.approvalProgress.processing')}</span>
              {ratio ? <span>{t('workflow.detail.approvalProgress.ratio', ratio)}</span> : null}
            </Space>
          )
        })}
      </Space>
    </div>
  )
}
