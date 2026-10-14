// 钉钉树节点卡:彩头 + 正文摘要 + 空态占位 + 右侧指向。对应 Vue 侧 WfNodeCard.vue。
import type { KeyboardEvent, MouseEvent } from 'react'
import { Button, Tooltip } from 'antd'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import { AppIcon } from '@/components/AppIcon'
import { useParticipantNames } from '@/workflow/participantNames'
import type { WfNode, WfNodeType } from '@/workflow/schema'
import { approvalCardGroups, countersignProgress, type WfApprovalNodeProgress } from '../../instance/approvalProgress'
import './wf-designer.css'
import '../../wf-identity.css'

const TONES: Record<WfNodeType, string> = {
  start: 'start',
  approval: 'approval',
  cc: 'cc',
  branch: 'branch',
  parallel: 'parallel',
  webhook: 'webhook',
  aiDecision: 'ai-decision',
}

const ICONS: Record<WfNodeType, string> = {
  start: 'ph:user',
  approval: 'ph:user-circle-check',
  cc: 'ph:paper-plane-tilt',
  branch: 'ph:git-branch',
  parallel: 'ph:git-merge',
  webhook: 'ph:webhooks-logo',
  aiDecision: 'ph:robot',
}

export interface WfNodeCardBody {
  text: string
  /** 空态占位(未配办理人/未配 URL):正文走浅灰。 */
  empty: boolean
}

function stringList(value: unknown): string[] {
  if (!Array.isArray(value)) return []
  return value.flatMap((item) => typeof item === 'string' && item.trim() ? [item.trim()] : [])
}

/** 优先用运行时下发的姓名；设计器只有用户 Id 时再查本地姓名表。 */
function recipientNames(params: Record<string, unknown>, known?: Record<string, string>): string | null {
  const embedded = stringList(params.userNames)
  if (embedded.length) return embedded.join('、')
  const ids = Array.isArray(params.userIds)
    ? params.userIds
    : params.userId != null
      ? [params.userId]
      : []
  const resolved = ids.flatMap((id) => {
    if (id == null || id === '') return []
    const name = known?.[String(id)]?.trim()
    return name ? [name] : []
  })
  return resolved.length ? resolved.join('、') : null
}

/** 节点正文摘要。纯函数(只依赖 node 与翻译器),与 Vue 侧 `body` computed 同语义。 */
export function nodeCardBody(
  node: WfNode,
  t: TFunction,
  participantNames?: Record<string, string>,
): WfNodeCardBody {
  if (node.type === 'start') {
    const scope = node.props?.initiatorScope ?? []
    if (!scope.length) return { text: t('workflow.node.placeholder.start'), empty: false }
    return { text: t('workflow.node.initiatorCount', { n: scope.length }), empty: false }
  }
  if (node.type === 'branch') {
    return { text: t('workflow.designer.armCount', { count: node.conditions?.length ?? 0 }), empty: false }
  }
  if (node.type === 'parallel') {
    return { text: t('workflow.designer.parallelArmCount', { count: node.parallelArms?.length ?? 0 }), empty: false }
  }
  if (node.type === 'webhook') {
    const url = node.props?.webhookUrl?.trim()
    return { text: url || t('workflow.node.placeholder.webhook'), empty: !url }
  }
  if (node.type === 'aiDecision') return { text: t('workflow.node.placeholder.aiDecision'), empty: false }

  const placeholder = node.type === 'cc'
    ? t('workflow.node.placeholder.cc')
    : t('workflow.node.placeholder.approval')
  const assignee = node.props?.assignee
  if (!assignee?.provider) return { text: placeholder, empty: true }

  const params = assignee.params ?? {}
  const providerLabel = t(`workflow.provider.${assignee.provider}`, { defaultValue: assignee.provider })

  switch (assignee.provider) {
    case 'user': {
      const named = recipientNames(params, participantNames)
      if (named) return { text: named, empty: false }
      // 姓名还没解析到时保留人数，避免已配置的节点看起来像没选人。
      const ids = Array.isArray(params.userIds)
        ? params.userIds
        : params.userId != null
          ? [params.userId]
          : []
      if (!ids.length) return { text: placeholder, empty: true }
      return { text: `${providerLabel} · ${ids.length}`, empty: false }
    }
    case 'role': {
      if (params.roleId == null && !(Array.isArray(params.roleIds) && params.roleIds.length)) {
        return { text: placeholder, empty: true }
      }
      return { text: providerLabel, empty: false }
    }
    case 'position': {
      if (params.positionId == null) return { text: placeholder, empty: true }
      return { text: providerLabel, empty: false }
    }
    case 'leader':
    case 'multiLeader': {
      const level = Number(params.level ?? 1) || 1
      return { text: `${providerLabel} · ${level}`, empty: false }
    }
    default:
      return { text: providerLabel, empty: false }
  }
}

export interface WfNodeCardProps {
  node: WfNode
  active?: boolean
  error?: boolean
  errorMessage?: string
  readonly?: boolean
  visited?: boolean
  current?: boolean
  /** 空审批人阻断:不要再画成橙色「已处理」。 */
  blocked?: boolean
  runtimeProgress?: WfApprovalNodeProgress
  onInspect?: () => void
  onSelect?: () => void
  onRemove?: () => void
}

export function WfNodeCard({
  node, active, error, errorMessage, readonly, visited, current, blocked, runtimeProgress, onInspect, onSelect, onRemove,
}: WfNodeCardProps) {
  const { t } = useTranslation()
  const participantNames = useParticipantNames()
  const body = nodeCardBody(node, t, participantNames)
  const title = node.name || t(`workflow.node.${node.type}`)
  const canInspect = readonly && node.type === 'approval' && !!runtimeProgress
  const onCurrentPath = !!visited || !!current
  const runtimeGroups = runtimeProgress && onCurrentPath ? approvalCardGroups(runtimeProgress) : []
  const countersign = runtimeProgress?.currentRounds
    .map((round) => countersignProgress(round))
    .find((item) => item != null)

  const classes = [
    'wf-card',
    `is-${TONES[node.type] ?? 'end'}`,
    active && !readonly ? 'is-active' : '',
    error ? 'is-error' : '',
    node.type === 'start' ? 'is-root' : '',
    readonly ? 'is-readonly' : '',
    readonly && blocked ? 'is-blocked' : '',
    readonly && visited ? 'is-visited' : '',
    readonly && current ? 'is-current' : '',
    readonly && !visited && !current && !blocked ? 'is-dimmed' : '',
  ].filter(Boolean).join(' ')

  // 只认卡片自身的按键(Vue 侧 `.self`):正文里的删除钮/输入不应触发选中。
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.target !== event.currentTarget) return
    if (canInspect && (event.key === 'Enter' || event.key === ' ' || event.key === 'Spacebar')) {
      event.preventDefault()
      onInspect?.()
      return
    }
    if (readonly) return
    if (event.key === 'Enter') onSelect?.()
    else if (event.key === ' ' || event.key === 'Spacebar') {
      event.preventDefault()
      onSelect?.()
    }
  }

  const remove = (event: MouseEvent<HTMLElement>) => {
    // 删除钮嵌在卡片里,不 stopPropagation 就会连带触发 onSelect 打开抽屉。
    event.stopPropagation()
    onRemove?.()
  }

  return (
    <div
      className={classes}
      role={!readonly || canInspect ? 'button' : undefined}
      tabIndex={!readonly || canInspect ? 0 : undefined}
      data-wf-node-id={node.id}
      data-wf-runtime-progress={runtimeProgress ? 'true' : undefined}
      data-wf-blocked={blocked ? 'true' : undefined}
      onClick={canInspect ? onInspect : readonly ? undefined : () => onSelect?.()}
      onKeyDown={onKeyDown}
    >
      <div className="wf-card-head">
        <AppIcon icon={ICONS[node.type] ?? 'ph:flag'} size={14} className="wf-card-head-icon" />
        <span className="wf-card-title">{title}</span>
        {runtimeProgress || blocked ? (
          <span className="wf-card-state">
            {blocked
              ? t('workflow.status.blocked')
              : onCurrentPath && runtimeProgress && runtimeProgress.currentRounds.length > 0
                ? t('workflow.status.running')
                : onCurrentPath
                  ? t('workflow.detail.approvalProgress.status.handled')
                  : t('workflow.detail.approvalProgress.notStarted')}
          </span>
        ) : null}
        {!readonly && node.type !== 'start' ? (
          <Tooltip title={t('common.delete')}>
            <Button
              type="text"
              size="small"
              className="wf-card-del"
              aria-label={t('common.delete')}
              icon={<AppIcon icon="ph:x" size={12} />}
              onClick={remove}
            />
          </Tooltip>
        ) : null}
      </div>
      <div className={`wf-card-body${readonly && node.type === 'approval' ? ' is-runtime' : ''}`}>
        {readonly && node.type === 'approval' ? (
          <div className="wf-card-progress">
            {countersign ? (
              <div className="wf-card-progress-row is-handled">
                <span className="wf-card-progress-label">{t('workflow.detail.approvalProgress.ratio', countersign)}</span>
              </div>
            ) : null}
            {blocked ? (
              <div className="wf-card-progress-row is-blocked" data-testid="wf-node-blocked">
                <span className="wf-card-progress-label">{t('workflow.status.blocked')}</span>
              </div>
            ) : runtimeGroups.length > 0 ? runtimeGroups.slice(0, 3).map((group) => (
              <div key={group.key} className={`wf-card-progress-row is-${group.key}`}>
                <span className="wf-card-progress-label">
                  {t(`workflow.detail.approvalProgress.status.${group.key}`)}
                </span>
                <span className="wf-card-progress-names">
                  {group.items.slice(0, 2).map((item) => (
                    `${item.automatic
                      ? t('workflow.detail.approvalProgress.automatic')
                      : item.name || t('workflow.detail.approvalProgress.system')}${item.automatic && item.name
                      ? ` (${t('workflow.detail.approvalProgress.onBehalfOf', { name: item.name })})`
                      : ''}${item.action ? ` · ${t(`workflow.action.${item.action}`)}` : ''}`
                  )).join('、')}
                  {group.items.length > 2 ? t('workflow.detail.approvalProgress.more', { n: group.items.length - 2 }) : ''}
                </span>
              </div>
            )) : (
              <span className="wf-card-text is-empty">
                {visited
                  ? t('workflow.detail.approvalProgress.noRoundData')
                  : t('workflow.detail.approvalProgress.notStarted')}
              </span>
            )}
          </div>
        ) : (
          <span className={`wf-card-text${body.empty ? ' is-empty' : ''}`}>{body.text}</span>
        )}
        {!readonly || canInspect ? <AppIcon icon="ph:caret-right" size={14} className="wf-card-arrow" /> : null}
      </div>
      {error && errorMessage ? <p className="wf-card-error">{errorMessage}</p> : null}
      {error ? (
        <div className="wf-card-warn" title={errorMessage || t('workflow.designer.invalid')}>
          <AppIcon icon="ph:warning-circle" size={18} />
        </div>
      ) : null}
    </div>
  )
}
