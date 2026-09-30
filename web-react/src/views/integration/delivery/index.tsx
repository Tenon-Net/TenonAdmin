// 可靠投递 = 状态筛选条(带计数)+ DataTable + 详情抽屉。状态语义分明:已受理待确认 ≠ 成功;待核对 = 结果未知且不能自动重发;
// 重试耗尽、失败需要人工处理。支持 ?deliveryKey= / ?businessKey= 深链(业务页可直接跳到自己的投递)。
import { useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { App, Badge, Button, Space, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import { useHasPerm } from '@/stores/auth'
import { integrationDeliveryApi } from '@/api/integration'
import { translateError } from '@/utils/error'
import { DeliveryStatus, type DeliveryRow } from '@/types/integration'
import { DeliveryDrawer, type DeliveryDrawerHandle } from './components/DeliveryDrawer'
import { PERM, deliveryStatusTag, enumLabel, fmtDateTime, outboundOutcomeTag } from '../shared'

const STATUS_ORDER = [
  DeliveryStatus.NeedsReconciliation, DeliveryStatus.Exhausted, DeliveryStatus.Failed, DeliveryStatus.AwaitingConfirmation,
  DeliveryStatus.Pending, DeliveryStatus.Dispatching, DeliveryStatus.Succeeded, DeliveryStatus.Cancelled,
]

/** 计数角标色:需要人工处理的红/橙,其余蓝。 */
const badgeColor = (status: number) => {
  const type = deliveryStatusTag[status]?.type
  return type === 'error' ? 'red' : type === 'warning' ? 'orange' : 'blue'
}

export default function IntegrationDeliveryPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const has = useHasPerm()
  const [searchParams, setSearchParams] = useSearchParams()
  const tableRef = useRef<DataTableHandle>(null)
  const drawerRef = useRef<DeliveryDrawerHandle>(null)

  // ── 状态筛选条:计数来自服务端汇总(有权限才取),每次表格加载后刷新;点击即筛选 ──
  const [counts, setCounts] = useState<Record<number, number>>({})
  const loadSummary = async () => {
    if (!has(PERM.deliverySummary)) return
    try {
      const rows = await integrationDeliveryApi.summary()
      setCounts(Object.fromEntries(rows.map((r) => [Number(r.status), Number(r.count)])))
    } catch (e) {
      message.error(translateError(e))
    }
  }

  // 深链 ?status= 只认已知状态值,非法值忽略(不把 NaN 发给后端)
  const statusText = searchParams.get('status')
  const statusValue = statusText ? Number(statusText) : Number.NaN
  const statusFilter = deliveryStatusTag[statusValue] ? statusValue : null
  // 深链带入的投递标识 / 业务键优先于搜索表单,所以以可关闭的标签显示出来,关掉即回到表单筛选
  const deliveryKey = searchParams.get('deliveryKey') || null
  const businessKey = searchParams.get('businessKey') || null
  const setParam = (key: string, value: string | null) => {
    const next = new URLSearchParams(searchParams)
    if (value == null) next.delete(key)
    else next.set(key, value)
    setSearchParams(next, { replace: true })
  }
  const setStatus = (status: number | null) => setParam('status', status == null ? null : String(status))
  const params = useMemo(
    () => ({
      ...(statusFilter != null ? { status: statusFilter } : {}),
      ...(deliveryKey ? { deliveryKey } : {}),
      ...(businessKey ? { businessKey } : {}),
    }),
    [statusFilter, deliveryKey, businessKey],
  )

  // fetcher 不 memo(ProTable 经 ref 读 request);搜索表单值逐字段收敛类型。
  const fetchDeliveries: PageFetcher<DeliveryRow> = async (q) => {
    const page = await integrationDeliveryApi.page({
      page: q.page,
      pageSize: q.pageSize,
      status: typeof q.status === 'number' ? q.status : undefined,
      adapter: typeof q.adapter === 'string' ? q.adapter : undefined,
      operation: typeof q.operation === 'string' ? q.operation : undefined,
      deliveryKey: typeof q.deliveryKey === 'string' ? q.deliveryKey : undefined,
      businessKey: typeof q.businessKey === 'string' ? q.businessKey : undefined,
      createTime: (q.createTime as [string, string] | undefined) ?? null,
    })
    void loadSummary()
    return page
  }

  const columns: ProColumns<DeliveryRow>[] = [
    { title: t('integration.delivery.createTime'), dataIndex: 'createTime', valueType: 'dateRange', width: 170, render: (_, r) => fmtDateTime(r.createTime) },
    { title: t('integration.delivery.deliveryKey'), dataIndex: 'deliveryKey', ellipsis: true },
    { title: t('integration.delivery.adapter'), dataIndex: 'adapter', width: 170 },
    { title: t('integration.delivery.operation'), dataIndex: 'operation', width: 130 },
    { title: t('integration.delivery.businessKey'), dataIndex: 'businessKey', width: 150, ellipsis: true, render: (_, r) => r.businessKey || '—' },
    {
      title: t('integration.delivery.status'), dataIndex: 'status', search: false, width: 130,
      render: (_, r) => {
        const tag = deliveryStatusTag[Number(r.status)]
        return tag ? <Tag variant="filled" color={tag.type}>{t(tag.key)}</Tag> : '—'
      },
    },
    {
      title: t('integration.delivery.attempts'), dataIndex: 'attemptCount', search: false, width: 90,
      render: (_, r) => t('integration.delivery.attemptsValue', { count: r.attemptsInBudget, max: r.maxAttempts }),
    },
    { title: t('integration.delivery.nextAttempt'), dataIndex: 'nextAttemptAt', search: false, width: 170, render: (_, r) => fmtDateTime(r.nextAttemptAt) },
    { title: t('integration.delivery.lastOutcome'), dataIndex: 'lastOutcome', search: false, width: 110, render: (_, r) => enumLabel(t, outboundOutcomeTag, r.lastOutcome) },
    { title: t('integration.delivery.lastError'), dataIndex: 'lastError', search: false, ellipsis: true, render: (_, r) => r.lastError || '—' },
    {
      title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 90, fixed: 'right',
      render: (_, r) =>
        has(PERM.deliveryGet) ? (
          <Button type="link" size="small" data-testid="delivery-detail" onClick={() => drawerRef.current?.open(r.id!)}>
            {t('integration.delivery.detail')}
          </Button>
        ) : null,
    },
  ]

  return (
    <>
      <DataTable<DeliveryRow>
        ref={tableRef}
        columns={columns}
        fetcher={fetchDeliveries}
        params={params}
        persistKey="integration-delivery"
        toolbar={
          <Space size={8} wrap>
            <Space size={6} wrap data-testid="delivery-status-filter">
              <Button size="small" type={statusFilter == null ? 'primary' : 'default'} onClick={() => setStatus(null)}>
                {t('integration.delivery.all')}
              </Button>
              {STATUS_ORDER.map((s) => (
                <Badge key={s} count={counts[s] ?? 0} overflowCount={999} size="small" color={badgeColor(s)}>
                  <Button size="small" type={statusFilter === s ? 'primary' : 'default'} onClick={() => setStatus(s)}>
                    {t(deliveryStatusTag[s].key)}
                  </Button>
                </Badge>
              ))}
            </Space>
            {deliveryKey && (
              <Tag closable color="processing" onClose={() => setParam('deliveryKey', null)}>
                {t('integration.delivery.deliveryKey')}:{deliveryKey}
              </Tag>
            )}
            {businessKey && (
              <Tag closable color="processing" onClose={() => setParam('businessKey', null)}>
                {t('integration.delivery.businessKey')}:{businessKey}
              </Tag>
            )}
          </Space>
        }
      />
      <DeliveryDrawer ref={drawerRef} onChanged={() => tableRef.current?.reload()} />
    </>
  )
}
