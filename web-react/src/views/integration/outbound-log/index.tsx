// 出站调用记录 = 只读 DataTable + 详情抽屉 + 出站目标清单。记录不含请求/响应正文与任何请求头(出站秘密只在请求头里);
// 地址不含查询串。支持从「可靠投递」详情带 ?deliveryId= / ?callId= 深链进入(刷新保持筛选)。
import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Alert, App, Button, Descriptions, Drawer, Space, Table, Tag, type TableColumnsType } from 'antd'
import { useTranslation } from 'react-i18next'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type PageFetcher } from '@/components/DataTable'
import { Can } from '@/components/Can'
import { integrationOutboundLogApi } from '@/api/integration'
import { translateError } from '@/utils/error'
import type { OutboundLogRow, OutboundTarget } from '@/types/integration'
import { PERM, fmtDateTime, methodTagType, outboundAuthTypeKey, outboundOutcomeTag } from '../shared'

export default function IntegrationOutboundLogPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const [searchParams, setSearchParams] = useSearchParams()
  const [detailRow, setDetailRow] = useState<OutboundLogRow | null>(null)

  /** 深链筛选:投递详情带入 deliveryId / callId;清除即回到全部。 */
  const linkedDeliveryId = searchParams.get('deliveryId') || null
  const linkedCallId = searchParams.get('callId') || null
  const params = useMemo(
    () => ({ ...(linkedDeliveryId ? { deliveryId: linkedDeliveryId } : {}), ...(linkedCallId ? { callId: linkedCallId } : {}) }),
    [linkedDeliveryId, linkedCallId],
  )
  const clearLink = (key: string) => {
    const next = new URLSearchParams(searchParams)
    next.delete(key)
    setSearchParams(next, { replace: true })
  }

  const outcomeOptions = Object.entries(outboundOutcomeTag).map(([value, tag]) => ({ label: t(tag.key), value: Number(value) }))
  const outcomeTag = (v?: number | null) => {
    const tag = v == null ? undefined : outboundOutcomeTag[Number(v)]
    return tag ? <Tag variant="filled" color={tag.type}>{t(tag.key)}</Tag> : '—'
  }

  const fetchLogs: PageFetcher<OutboundLogRow> = (q) =>
    integrationOutboundLogApi.page({
      page: q.page,
      pageSize: q.pageSize,
      target: typeof q.target === 'string' ? q.target : undefined,
      operation: typeof q.operation === 'string' ? q.operation : undefined,
      outcome: typeof q.outcome === 'number' ? q.outcome : undefined,
      deliveryId: typeof q.deliveryId === 'string' || typeof q.deliveryId === 'number' ? q.deliveryId : undefined,
      callId: typeof q.callId === 'string' ? q.callId : undefined,
      traceId: typeof q.traceId === 'string' ? q.traceId : undefined,
      createTime: (q.createTime as [string, string] | undefined) ?? null,
    })

  const columns: ProColumns<OutboundLogRow>[] = [
    { title: t('integration.outboundLog.time'), dataIndex: 'createTime', valueType: 'dateRange', width: 170, render: (_, r) => fmtDateTime(r.createTime) },
    { title: t('integration.outboundLog.target'), dataIndex: 'target', width: 130 },
    { title: t('integration.outboundLog.operation'), dataIndex: 'operation', width: 140, render: (_, r) => r.operation || '—' },
    {
      title: t('integration.outboundLog.method'), dataIndex: 'httpMethod', search: false, width: 90,
      render: (_, r) => <Tag variant="filled" color={methodTagType(r.httpMethod)}>{r.httpMethod}</Tag>,
    },
    { title: t('integration.outboundLog.url'), dataIndex: 'url', search: false, ellipsis: true },
    { title: t('integration.outboundLog.status'), dataIndex: 'statusCode', search: false, width: 100, render: (_, r) => (r.statusCode == null ? '—' : String(r.statusCode)) },
    {
      title: t('integration.outboundLog.outcome'), dataIndex: 'outcome', valueType: 'select', width: 110,
      fieldProps: { allowClear: true, options: outcomeOptions },
      render: (_, r) => outcomeTag(r.outcome),
    },
    { title: t('integration.outboundLog.reason'), dataIndex: 'reason', search: false, width: 150, ellipsis: true, render: (_, r) => r.reason || '—' },
    { title: t('integration.outboundLog.elapsed'), dataIndex: 'elapsedMs', search: false, width: 100, render: (_, r) => `${r.elapsedMs} ms` },
    { title: t('integration.outboundLog.callId'), dataIndex: 'callId', hideInTable: true },
    { title: t('integration.outboundLog.traceId'), dataIndex: 'traceId', hideInTable: true },
    {
      title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 90, fixed: 'right',
      render: (_, r) => <Button type="link" size="small" onClick={() => setDetailRow(r)}>{t('integration.outboundLog.detail')}</Button>,
    },
  ]

  // ── 出站目标清单(只回答是否取得到凭据,永不显示凭据)──
  const [targetsOpen, setTargetsOpen] = useState(false)
  const [targets, setTargets] = useState<OutboundTarget[]>([])
  const [targetsLoading, setTargetsLoading] = useState(false)
  const openTargets = async () => {
    setTargetsOpen(true)
    setTargetsLoading(true)
    try {
      setTargets(await integrationOutboundLogApi.targets())
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setTargetsLoading(false)
    }
  }
  const targetColumns: TableColumnsType<OutboundTarget> = [
    { title: t('integration.outboundLog.targetName'), dataIndex: 'name', width: 140 },
    { title: t('integration.outboundLog.baseUrl'), dataIndex: 'baseUrl', ellipsis: true },
    { title: t('integration.outboundLog.timeout'), dataIndex: 'timeoutSeconds', width: 80, render: (_, r) => `${r.timeoutSeconds}s` },
    {
      title: t('integration.outboundLog.auth'), dataIndex: 'authType', width: 150,
      render: (_, r) => {
        const label = t(outboundAuthTypeKey[Number(r.authType ?? 0)] ?? 'integration.outbound.authNone')
        return r.authHeaderName ? `${label}(${r.authHeaderName})` : label
      },
    },
    {
      title: t('integration.outboundLog.trustedCidrs'), dataIndex: 'trustedCidrs', width: 180,
      render: (_, r) =>
        r.trustedCidrs?.length
          ? <Space size={4} wrap>{r.trustedCidrs.map((c) => <Tag key={c} variant="filled">{c}</Tag>)}</Space>
          : t('integration.outboundLog.noTrusted'),
    },
    {
      title: t('integration.outboundLog.credential'), dataIndex: 'hasCredential', width: 90,
      render: (_, r) => (
        <Tag variant="filled" color={r.hasCredential ? 'success' : 'default'}>
          {t(r.hasCredential ? 'integration.outboundLog.credentialSet' : 'integration.outboundLog.credentialMissing')}
        </Tag>
      ),
    },
  ]

  return (
    <>
      <DataTable<OutboundLogRow>
        columns={columns}
        fetcher={fetchLogs}
        params={params}
        persistKey="integration-outbound-log"
        toolbar={
          <Space size={8}>
            <Can code={PERM.outboundTargets}>
              <Button data-testid="outbound-targets" onClick={() => void openTargets()}>{t('integration.outboundLog.targets')}</Button>
            </Can>
            {linkedDeliveryId && (
              <Tag closable color="processing" onClose={() => clearLink('deliveryId')}>
                {t('integration.outboundLog.filterDelivery')}:{linkedDeliveryId}
              </Tag>
            )}
            {linkedCallId && (
              <Tag closable color="processing" onClose={() => clearLink('callId')}>
                {t('integration.outboundLog.filterCall')}:{linkedCallId}
              </Tag>
            )}
          </Space>
        }
      />
      <Drawer open={!!detailRow} onClose={() => setDetailRow(null)} title={t('integration.outboundLog.detail')} size={560}>
        {detailRow && (
          <Descriptions column={1} bordered size="small" items={[
            { key: 'time', label: t('integration.outboundLog.time'), children: fmtDateTime(detailRow.createTime) },
            { key: 'target', label: t('integration.outboundLog.target'), children: detailRow.target },
            { key: 'operation', label: t('integration.outboundLog.operation'), children: detailRow.operation || '—' },
            { key: 'method', label: t('integration.outboundLog.method'), children: detailRow.httpMethod },
            { key: 'url', label: t('integration.outboundLog.url'), children: detailRow.url },
            { key: 'status', label: t('integration.outboundLog.status'), children: detailRow.statusCode ?? '—' },
            { key: 'outcome', label: t('integration.outboundLog.outcome'), children: outcomeTag(detailRow.outcome) },
            { key: 'reason', label: t('integration.outboundLog.reason'), children: detailRow.reason || '—' },
            { key: 'errorSummary', label: t('integration.outboundLog.errorSummary'), children: detailRow.errorSummary || '—' },
            { key: 'elapsed', label: t('integration.outboundLog.elapsed'), children: `${detailRow.elapsedMs} ms` },
            { key: 'callId', label: t('integration.outboundLog.callId'), children: detailRow.callId },
            { key: 'traceId', label: t('integration.outboundLog.traceId'), children: detailRow.traceId || '—' },
            {
              key: 'delivery', label: t('integration.outboundLog.delivery'),
              children: detailRow.deliveryId ? `${detailRow.deliveryId} · #${detailRow.attemptNo ?? '—'}` : '—',
            },
          ]} />
        )}
      </Drawer>
      <Drawer open={targetsOpen} onClose={() => setTargetsOpen(false)} title={t('integration.outboundLog.targetsTitle')} size={860}>
        <Alert type="info" title={t('integration.outboundLog.targetsHint')} style={{ marginBottom: 12 }} />
        <Table<OutboundTarget>
          rowKey={(r) => r.name ?? ''}
          columns={targetColumns}
          dataSource={targets}
          loading={targetsLoading}
          pagination={false}
          size="small"
        />
      </Drawer>
    </>
  )
}
