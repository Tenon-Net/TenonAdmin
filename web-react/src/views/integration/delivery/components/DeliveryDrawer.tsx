// 投递详情抽屉:记录全貌 + 服务端判定的可用操作 + 尝试时间线。
// 按钮先按权限显示,是否可点只看服务端返回的 allowedActions——页面不自行判断「这次重试安全不安全」;
// 执行时回传页面看到的栅栏值;记录已被投递器或他人改动(49046)或状态已不允许(49045)时关掉操作弹窗并刷新,
// 由操作人按最新状态重新判断。
import { forwardRef, useCallback, useImperativeHandle, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Alert, App, Button, Descriptions, Drawer, Empty, Form, Input, Space, Spin, Table, Tag, type TableColumnsType } from 'antd'
import { useTranslation } from 'react-i18next'
import { CodeBlock } from '@/components/CodeBlock'
import { FormContainer } from '@/components/FormContainer'
import { useHasPerm } from '@/stores/auth'
import { ApiError } from '@/api'
import { integrationDeliveryApi } from '@/api/integration'
import { translateError } from '@/utils/error'
import { DeliveryActions, DeliveryStatus, type DeliveryAction, type DeliveryAttempt, type DeliveryDetail } from '@/types/integration'
import {
  DELIVERY_ACTION_PERM, PERM, attemptKindKey, attemptOutcomeKey, attemptTriggerKey, deliveryActionKey, deliveryStatusHint,
  deliveryStatusTag, enumLabel, fmtDateTime, outboundOutcomeTag, type TagType,
} from '../../shared'

export interface DeliveryDrawerHandle {
  open: (id: number | string) => void
}

/** 按钮顺序;有权限才显示,是否可点由服务端 allowedActions 决定。 */
const ACTION_ORDER: DeliveryAction[] = [
  DeliveryActions.Retry, DeliveryActions.Query, DeliveryActions.ConfirmSucceeded, DeliveryActions.ConfirmNotExecuted, DeliveryActions.Cancel,
]
const ACTION_HINT_KEY: Record<DeliveryAction, string> = {
  [DeliveryActions.Retry]: 'integration.delivery.actionHintRetry',
  [DeliveryActions.Query]: 'integration.delivery.actionHintQuery',
  [DeliveryActions.ConfirmSucceeded]: 'integration.delivery.actionHintConfirmSucceeded',
  [DeliveryActions.ConfirmNotExecuted]: 'integration.delivery.actionHintConfirmNotExecuted',
  [DeliveryActions.Cancel]: 'integration.delivery.actionHintCancel',
}
const needsNote = (a: DeliveryAction) => a === DeliveryActions.ConfirmSucceeded || a === DeliveryActions.ConfirmNotExecuted
/** 服务端判定「当前状态不允许」(49045)或「记录已被改动」(49046):页面所见已过期,须刷新后重新审视。 */
const STALE_VIEW_CODES = [49045, 49046]

const sectionTitle = { margin: '16px 0 8px', fontSize: 14, fontWeight: 600 } as const

export const DeliveryDrawer = forwardRef<DeliveryDrawerHandle, { onChanged: () => void }>(function DeliveryDrawer({ onChanged }, ref) {
  const { t, i18n } = useTranslation()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const has = useHasPerm()

  const [open, setOpen] = useState(false)
  const [loading, setLoading] = useState(false)
  const [detail, setDetail] = useState<DeliveryDetail | null>(null)
  const currentId = useRef<number | string>(0)
  const loadSeq = useRef(0)

  /** 取详情:只认最后一次请求且仍是当前记录的响应,快速切换记录时迟到的旧响应一律丢弃。 */
  const load = useCallback(async (id: number | string) => {
    const seq = ++loadSeq.current
    setLoading(true)
    try {
      const loaded = await integrationDeliveryApi.get(id)
      if (seq === loadSeq.current && id === currentId.current) setDetail(loaded)
    } catch (e) {
      if (seq === loadSeq.current) message.error(translateError(e))
    } finally {
      if (seq === loadSeq.current) setLoading(false)
    }
  }, [message])

  useImperativeHandle(ref, () => ({
    open: (id) => {
      currentId.current = id
      setDetail(null)
      setOpen(true)
      void load(id)
    },
  }), [load])

  const statusTag = detail ? deliveryStatusTag[Number(detail.status)] : undefined
  const hintKey = detail ? deliveryStatusHint[Number(detail.status)] : undefined
  const hintType = detail?.status === DeliveryStatus.AwaitingConfirmation || detail?.status === DeliveryStatus.Dispatching ? 'info' : 'warning'
  const visibleActions = ACTION_ORDER.filter((a) => has(DELIVERY_ACTION_PERM[a]))
  const allowed = useMemo(() => new Set(detail?.allowedActions ?? []), [detail])

  // ── 人工操作弹窗 ──
  const [actionOpen, setActionOpen] = useState(false)
  const [pending, setPending] = useState<DeliveryAction>(DeliveryActions.Retry)
  const [note, setNote] = useState('')
  const openAction = (action: DeliveryAction) => {
    setPending(action)
    setNote('')
    setActionOpen(true)
  }
  const doAction = async () => {
    const target = detail
    if (!target?.id) return false
    const text = note.trim()
    if (needsNote(pending) && !text) {
      message.warning(t('integration.delivery.noteRequired'))
      return false
    }
    try {
      // 目标 id 与栅栏取自同一份已加载的详情:不会把一条记录的栅栏发给另一条
      const updated = await integrationDeliveryApi.act(target.id, pending, { note: text || null, fence: target.fence ?? null })
      if (target.id === currentId.current) {
        loadSeq.current++   // 作废操作前发出、尚未返回的刷新,免得旧响应盖掉操作结果
        setLoading(false)
        setDetail(updated)
      }
      message.success(t('integration.delivery.actionDone'))
      onChanged()
    } catch (e) {
      message.error(translateError(e))
      if (e instanceof ApiError && STALE_VIEW_CODES.includes(e.code)) {
        void load(target.id)   // 关弹窗并刷新详情,操作人按最新状态重新判断,不带着新栅栏静默重发
        return
      }
      return false   // 其他错误(如 49047 缺说明)留在弹窗里修改
    }
  }

  const viewCalls = (query: Record<string, string>) => {
    setOpen(false)
    navigate(`/integration/outbound-log?${new URLSearchParams(query).toString()}`)
  }

  const outcomeText = (value?: string | null) => {
    if (!value) return '—'
    const key = attemptOutcomeKey[value]
    return key && i18n.exists(key) ? t(key) : value
  }
  const payloadText = useMemo(() => {
    const raw = detail?.payloadJson
    if (!raw) return ''
    try {
      return JSON.stringify(JSON.parse(raw), null, 2)
    } catch {
      return raw
    }
  }, [detail])
  const capabilityTags = (): { text: string; type: TagType }[] => {
    if (!detail) return []
    if (!detail.adapterRegistered) return [{ text: t('integration.delivery.adapterMissing'), type: 'error' }]
    const tags: { text: string; type: TagType }[] = []
    if (detail.supportsIdempotency) tags.push({ text: t('integration.delivery.capIdempotency'), type: 'success' })
    if (detail.supportsQuery) tags.push({ text: t('integration.delivery.capQuery'), type: 'processing' })
    if (!tags.length) tags.push({ text: t('integration.delivery.capNone'), type: 'default' })
    return tags
  }

  const attemptColumns: TableColumnsType<DeliveryAttempt> = [
    { title: t('integration.delivery.attemptTime'), dataIndex: 'startedAt', width: 160, render: (_, r) => fmtDateTime(r.startedAt) },
    { title: t('integration.delivery.attemptNo'), dataIndex: 'attemptNo', width: 60 },
    { title: t('integration.delivery.attemptKind'), dataIndex: 'kind', width: 90, render: (_, r) => enumLabel(t, attemptKindKey, r.kind) },
    { title: t('integration.delivery.attemptTrigger'), dataIndex: 'trigger', width: 90, render: (_, r) => enumLabel(t, attemptTriggerKey, r.trigger) },
    { title: t('integration.delivery.attemptOutcome'), dataIndex: 'outcome', width: 120, render: (_, r) => outcomeText(r.outcome) },
    { title: t('integration.delivery.httpStatus'), dataIndex: 'httpStatus', width: 90, render: (_, r) => (r.httpStatus == null ? '—' : String(r.httpStatus)) },
    {
      title: t('integration.delivery.callId'), dataIndex: 'callId', width: 110,
      render: (_, r) =>
        r.callId && has(PERM.outboundLog)
          ? <Button type="link" size="small" style={{ padding: 0 }} onClick={() => viewCalls({ callId: r.callId! })}>{r.callId.slice(0, 8)}</Button>
          : r.callId ? r.callId.slice(0, 8) : '—',
    },
    { title: t('integration.delivery.operator'), dataIndex: 'operatorName', width: 100, render: (_, r) => r.operatorName || '—' },
    { title: t('integration.delivery.attemptNote'), dataIndex: 'note', ellipsis: true, render: (_, r) => r.note || '—' },
    { title: t('integration.delivery.errorSummary'), dataIndex: 'errorSummary', ellipsis: true, render: (_, r) => r.errorSummary || '—' },
  ]

  return (
    <>
      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        size={900}
        title={
          <Space size={8}>
            <span>{t('integration.delivery.detail')}</span>
            {statusTag && <Tag variant="filled" color={statusTag.type} data-testid="delivery-status">{t(statusTag.key)}</Tag>}
          </Space>
        }
      >
        <Spin spinning={loading}>
          {detail && (
            <>
              {hintKey && <Alert type={hintType} title={t(hintKey)} style={{ marginBottom: 12 }} data-testid="delivery-hint" />}

              <Space size={8} wrap style={{ marginBottom: 12 }}>
                {visibleActions.map((a) => (
                  <Button
                    key={a}
                    size="small"
                    type={a === DeliveryActions.Retry ? 'primary' : 'default'}
                    disabled={!allowed.has(a)}
                    data-testid={`delivery-action-${a}`}
                    onClick={() => openAction(a)}
                  >
                    {t(deliveryActionKey[a])}
                  </Button>
                ))}
                {has(PERM.outboundLog) && (
                  <Button size="small" type="text" onClick={() => viewCalls({ deliveryId: String(detail.id) })}>
                    {t('integration.delivery.viewCalls')}
                  </Button>
                )}
                <Button size="small" type="text" onClick={() => void load(currentId.current)}>{t('integration.delivery.refresh')}</Button>
              </Space>

              <Descriptions column={2} bordered size="small" items={[
                { key: 'deliveryKey', label: t('integration.delivery.deliveryKey'), span: 2, children: <span data-testid="delivery-key">{detail.deliveryKey}</span> },
                { key: 'adapter', label: t('integration.delivery.adapter'), children: detail.adapter },
                {
                  key: 'capabilities', label: t('integration.delivery.capabilities'),
                  children: (
                    <Space size={4} wrap>
                      {capabilityTags().map((c) => <Tag key={c.text} variant="filled" color={c.type}>{c.text}</Tag>)}
                    </Space>
                  ),
                },
                { key: 'operation', label: t('integration.delivery.operation'), children: detail.operation },
                { key: 'businessKey', label: t('integration.delivery.businessKey'), children: detail.businessKey || '—' },
                {
                  key: 'attempts', label: t('integration.delivery.attempts'),
                  children: (
                    <>
                      {t('integration.delivery.attemptsValue', { count: detail.attemptsInBudget, max: detail.maxAttempts })}
                      <span style={{ color: 'var(--color-text-secondary)', fontSize: 12 }}>
                        {' · '}{t('integration.delivery.attemptsTotal', { n: detail.attemptCount })}
                      </span>
                    </>
                  ),
                },
                { key: 'nextAttempt', label: t('integration.delivery.nextAttempt'), children: fmtDateTime(detail.nextAttemptAt) },
                { key: 'lastOutcome', label: t('integration.delivery.lastOutcome'), children: enumLabel(t, outboundOutcomeTag, detail.lastOutcome) },
                { key: 'remoteReference', label: t('integration.delivery.remoteReference'), children: detail.remoteReference || '—' },
                { key: 'lastError', label: t('integration.delivery.lastError'), span: 2, children: detail.lastError || '—' },
                { key: 'createTime', label: t('integration.delivery.createTime'), children: fmtDateTime(detail.createTime) },
                { key: 'completedAt', label: t('integration.delivery.completedAt'), children: fmtDateTime(detail.completedAt) },
                { key: 'deadline', label: t('integration.delivery.deadline'), children: fmtDateTime(detail.deadlineAt) },
                { key: 'confirmDeadline', label: t('integration.delivery.confirmDeadline'), children: fmtDateTime(detail.confirmDeadlineAt) },
                ...(detail.leaseUntil
                  ? [{ key: 'lease', label: t('integration.delivery.lease'), span: 2, children: `${detail.leaseOwner || '—'} · ${fmtDateTime(detail.leaseUntil)}` }]
                  : []),
                ...(detail.resolutionNote
                  ? [{ key: 'resolutionNote', label: t('integration.delivery.resolutionNote'), span: 2, children: detail.resolutionNote }]
                  : []),
              ]} />

              <h4 style={sectionTitle}>{t('integration.delivery.payload')}</h4>
              {payloadText && <CodeBlock code={payloadText} />}

              <h4 style={sectionTitle}>{t('integration.delivery.attempts')}</h4>
              {detail.attempts?.length ? (
                <Table<DeliveryAttempt>
                  rowKey={(r) => String(r.id)}
                  columns={attemptColumns}
                  dataSource={detail.attempts}
                  pagination={false}
                  size="small"
                  scroll={{ x: 1100 }}
                  data-testid="delivery-attempts"
                />
              ) : (
                <Empty description={t('integration.delivery.noAttempts')} />
              )}
            </>
          )}
        </Spin>
      </Drawer>

      <FormContainer
        open={actionOpen}
        onOpenChange={setActionOpen}
        variant="modal"
        title={t(deliveryActionKey[pending])}
        width={480}
        onConfirm={doAction}
        confirmText={t(deliveryActionKey[pending])}
      >
        <Alert type="info" title={t(ACTION_HINT_KEY[pending])} style={{ marginBottom: 12 }} />
        <Form layout="vertical">
          <Form.Item label={t('integration.delivery.note')} required={needsNote(pending)}>
            <Input.TextArea
              value={note}
              onChange={(e) => setNote(e.target.value)}
              maxLength={256}
              showCount
              autoSize={{ minRows: 3, maxRows: 6 }}
              placeholder={t('integration.delivery.notePlaceholder')}
              data-testid="delivery-action-note"
            />
          </Form.Item>
        </Form>
      </FormContainer>
    </>
  )
})
