// 钉钉树节点卡:彩头 + 正文摘要 + 空态占位 + 右侧指向。对应 Vue 侧 WfNodeCard.vue。
import type { KeyboardEvent, MouseEvent } from 'react'
import { Button, Tooltip } from 'antd'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import { AppIcon } from '@/components/AppIcon'
import type { WfNode, WfNodeType } from '@/workflow/schema'
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

/** 节点正文摘要。纯函数(只依赖 node 与翻译器),与 Vue 侧 `body` computed 同语义。 */
export function nodeCardBody(node: WfNode, t: TFunction): WfNodeCardBody {
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
  readonly?: boolean
  visited?: boolean
  current?: boolean
  onSelect?: () => void
  onRemove?: () => void
}

export function WfNodeCard({ node, active, error, readonly, visited, current, onSelect, onRemove }: WfNodeCardProps) {
  const { t } = useTranslation()
  const body = nodeCardBody(node, t)
  const title = node.name || t(`workflow.node.${node.type}`)

  const classes = [
    'wf-card',
    `is-${TONES[node.type] ?? 'end'}`,
    active && !readonly ? 'is-active' : '',
    error ? 'is-error' : '',
    node.type === 'start' ? 'is-root' : '',
    readonly ? 'is-readonly' : '',
    readonly && visited ? 'is-visited' : '',
    readonly && current ? 'is-current' : '',
    readonly && !visited && !current ? 'is-dimmed' : '',
  ].filter(Boolean).join(' ')

  // 只认卡片自身的按键(Vue 侧 `.self`):正文里的删除钮/输入不应触发选中。
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (readonly || event.target !== event.currentTarget) return
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
      role={readonly ? undefined : 'button'}
      tabIndex={readonly ? undefined : 0}
      onClick={readonly ? undefined : () => onSelect?.()}
      onKeyDown={onKeyDown}
    >
      <div className="wf-card-head">
        <AppIcon icon={ICONS[node.type] ?? 'ph:flag'} size={14} className="wf-card-head-icon" />
        <span className="wf-card-title">{title}</span>
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
      <div className="wf-card-body">
        <span className={`wf-card-text${body.empty ? ' is-empty' : ''}`}>{body.text}</span>
        <AppIcon icon="ph:caret-right" size={14} className="wf-card-arrow" />
      </div>
      {error ? (
        <div className="wf-card-warn" title={t('workflow.designer.invalid')}>
          <AppIcon icon="ph:warning-circle" size={18} />
        </div>
      ) : null}
    </div>
  )
}
