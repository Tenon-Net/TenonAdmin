// 开放调用记录 = 只读 DataTable + 详情抽屉。记录不含请求/响应正文与任何认证头;凭据只以公开标识出现。
// 支持从接入应用页带 ?appId= 深链进入(刷新保持筛选)。
import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { App, Button, Descriptions, Drawer, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type PageFetcher } from '@/components/DataTable'
import { integrationAppApi, integrationInboundLogApi } from '@/api/integration'
import { useHasPerm } from '@/stores/auth'
import { translateError } from '@/utils/error'
import type { InboundLogRow } from '@/types/integration'
import { PERM, fmtDateTime, inboundOutcomeTag, methodTagType } from '../shared'

export default function IntegrationInboundLogPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const has = useHasPerm()
  const [searchParams, setSearchParams] = useSearchParams()
  const [appOptions, setAppOptions] = useState<{ label: string; value: string }[]>([])
  const [detailRow, setDetailRow] = useState<InboundLogRow | null>(null)

  /** 应用下拉要「接入应用-分页」权限;没有就不取、也不给按应用筛选(深链带入的 ?appId= 仍然生效)。 */
  const canListApps = has(PERM.appPage)
  useEffect(() => {
    if (!canListApps) return
    integrationAppApi
      .page({ page: 1, pageSize: 200 })
      .then((p) => setAppOptions(p.items.map((a) => ({ label: `${a.name}(${a.code})`, value: String(a.id) }))))
      .catch((e: unknown) => message.error(translateError(e)))
  }, [message, canListApps])

  /** 深链筛选:?appId= 由接入应用页「调用记录」带入;清除即回到全部。 */
  const linkedAppId = searchParams.get('appId') || null
  const params = useMemo(() => (linkedAppId ? { appId: linkedAppId } : {}), [linkedAppId])
  const linkedAppLabel = appOptions.find((o) => o.value === linkedAppId)?.label ?? linkedAppId

  const outcomeOptions = Object.entries(inboundOutcomeTag).map(([value, tag]) => ({ label: t(tag.key), value: Number(value) }))
  const outcomeTag = (v?: number | null) => {
    const tag = v == null ? undefined : inboundOutcomeTag[Number(v)]
    return tag ? <Tag variant="filled" color={tag.type}>{t(tag.key)}</Tag> : '—'
  }

  const fetchLogs: PageFetcher<InboundLogRow> = (q) =>
    integrationInboundLogApi.page({
      page: q.page,
      pageSize: q.pageSize,
      appId: typeof q.appId === 'string' || typeof q.appId === 'number' ? q.appId : undefined,
      route: typeof q.route === 'string' ? q.route : undefined,
      outcome: typeof q.outcome === 'number' ? q.outcome : undefined,
      traceId: typeof q.traceId === 'string' ? q.traceId : undefined,
      createTime: (q.createTime as [string, string] | undefined) ?? null,
    })

  const columns: ProColumns<InboundLogRow>[] = [
    { title: t('integration.inboundLog.time'), dataIndex: 'createTime', valueType: 'dateRange', width: 170, render: (_, r) => fmtDateTime(r.createTime) },
    {
      title: t('integration.inboundLog.app'), dataIndex: 'appId', valueType: 'select', search: canListApps ? undefined : false,
      fieldProps: { allowClear: true, showSearch: true, options: appOptions },
      render: (_, r) => r.appCode || '—',
    },
    {
      title: t('integration.inboundLog.method'), dataIndex: 'httpMethod', search: false, width: 90,
      render: (_, r) => <Tag variant="filled" color={methodTagType(r.httpMethod)}>{r.httpMethod}</Tag>,
    },
    { title: t('integration.inboundLog.route'), dataIndex: 'route', ellipsis: true, render: (_, r) => r.route || r.path },
    { title: t('integration.inboundLog.status'), dataIndex: 'statusCode', search: false, width: 90 },
    { title: t('integration.inboundLog.resultCode'), dataIndex: 'resultCode', search: false, width: 100 },
    {
      title: t('integration.inboundLog.outcome'), dataIndex: 'outcome', valueType: 'select', width: 110,
      fieldProps: { allowClear: true, options: outcomeOptions },
      render: (_, r) => outcomeTag(r.outcome),
    },
    { title: t('integration.inboundLog.reason'), dataIndex: 'failureReason', search: false, ellipsis: true, render: (_, r) => r.failureReason || '—' },
    { title: t('integration.inboundLog.elapsed'), dataIndex: 'elapsedMs', search: false, width: 100, render: (_, r) => `${r.elapsedMs} ms` },
    { title: t('integration.inboundLog.traceId'), dataIndex: 'traceId', ellipsis: true },
    {
      title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 90, fixed: 'right',
      render: (_, r) => <Button type="link" size="small" onClick={() => setDetailRow(r)}>{t('integration.inboundLog.detail')}</Button>,
    },
  ]

  return (
    <>
      <DataTable<InboundLogRow>
        columns={columns}
        fetcher={fetchLogs}
        params={params}
        persistKey="integration-inbound-log"
        toolbar={
          linkedAppId ? (
            <Tag closable color="processing" onClose={() => setSearchParams({})}>
              {t('integration.inboundLog.app')}:{linkedAppLabel}
            </Tag>
          ) : null
        }
      />
      <Drawer open={!!detailRow} onClose={() => setDetailRow(null)} title={t('integration.inboundLog.detail')} size={560}>
        {detailRow && (
          <Descriptions column={1} bordered size="small" items={[
            { key: 'time', label: t('integration.inboundLog.time'), children: fmtDateTime(detailRow.createTime) },
            { key: 'app', label: t('integration.inboundLog.app'), children: detailRow.appCode || '—' },
            { key: 'keyId', label: t('integration.inboundLog.keyId'), children: detailRow.keyId ? `tna_${detailRow.keyId}.****` : '—' },
            { key: 'route', label: t('integration.inboundLog.route'), children: detailRow.route || '—' },
            { key: 'path', label: t('integration.inboundLog.path'), children: detailRow.path },
            { key: 'status', label: t('integration.inboundLog.status'), children: detailRow.statusCode },
            { key: 'resultCode', label: t('integration.inboundLog.resultCode'), children: detailRow.resultCode },
            { key: 'outcome', label: t('integration.inboundLog.outcome'), children: outcomeTag(detailRow.outcome) },
            { key: 'reason', label: t('integration.inboundLog.reason'), children: detailRow.failureReason || '—' },
            { key: 'elapsed', label: t('integration.inboundLog.elapsed'), children: `${detailRow.elapsedMs} ms` },
            { key: 'traceId', label: t('integration.inboundLog.traceId'), children: detailRow.traceId },
            { key: 'requestId', label: t('integration.inboundLog.requestId'), children: detailRow.clientRequestId || '—' },
            { key: 'clientIp', label: t('integration.inboundLog.clientIp'), children: detailRow.clientIp || '—' },
          ]} />
        )}
      </Drawer>
    </>
  )
}
