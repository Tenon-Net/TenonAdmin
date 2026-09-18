// 流程监控。菜单 component 填 `workflow/monitor/index`。
// 参与筛选(发起人/办理人/抄送人)是业务过滤,不是数据权限;行点击进同一实例详情。
import { useCallback, useMemo } from 'react'
import { Button, Space, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type PageFetcher } from '@/components/DataTable'
import { UserSelect } from '@/components/UserSelect'
import { wfInstanceApi } from '@/api/workflow'
import { normalizeWfId } from '@/workflow/id'
import { formatDateTime, instanceStatusColor, normalizeInstanceStatus } from '@/workflow/statusLabels'
import type { WfInstanceListItem } from '@/types/workflow'

/** 实例状态 1–5(与 statusLabels 的数字表同源)。 */
const STATUS_VALUES = [1, 2, 3, 4, 5] as const

export default function WfMonitorPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()

  const fetchMonitor: PageFetcher<WfInstanceListItem> = (q) =>
    wfInstanceApi.monitor({
      page: q.page,
      pageSize: q.pageSize,
      status: typeof q.status === 'number' ? q.status : undefined,
      businessKey: typeof q.businessKey === 'string' ? q.businessKey : undefined,
      starterUserId: normalizeWfId(q.starterUserId) ?? undefined,
      actorUserId: normalizeWfId(q.actorUserId) ?? undefined,
      ccUserId: normalizeWfId(q.ccUserId) ?? undefined,
    })

  const openDetail = useCallback(
    (r: WfInstanceListItem) => {
      if (r.id == null) return
      navigate(`/workflow/instance/${r.id}/detail`)
    },
    [navigate],
  )

  const columns = useMemo<ProColumns<WfInstanceListItem>[]>(
    () => [
      { title: t('workflow.monitor.definition'), dataIndex: 'definitionName', minWidth: 160, ellipsis: true, search: false },
      {
        title: t('workflow.monitor.version'), dataIndex: 'version', width: 80, align: 'center', search: false,
        render: (_, r) => (r.version == null ? '—' : `v${r.version}`),
      },
      {
        title: t('workflow.monitor.starter'), dataIndex: 'starterUserId', width: 140,
        // 裸返 UserSelect:value/onChange 由 pro-components cloneElement 注入,显式写会盖掉注入。
        formItemRender: () => <UserSelect placeholder={t('workflow.monitor.starter')} />,
        render: (_, r) => (r.starterUserId == null ? '—' : t('workflow.detail.userFallback', { id: r.starterUserId })),
      },
      {
        // 办理人/抄送人只作筛选条件,列表无对应列(一条实例可有多个办理人,摊不进一格)。
        title: t('workflow.monitor.actor'), dataIndex: 'actorUserId', hideInTable: true, hideInSetting: true,
        formItemRender: () => <UserSelect placeholder={t('workflow.monitor.actor')} />,
      },
      {
        title: t('workflow.monitor.cc'), dataIndex: 'ccUserId', hideInTable: true, hideInSetting: true,
        formItemRender: () => <UserSelect placeholder={t('workflow.monitor.cc')} />,
      },
      {
        title: t('workflow.monitor.businessKey'), dataIndex: 'businessKey', width: 140, ellipsis: true,
        render: (_, r) => r.businessKey || '—',
      },
      {
        title: t('common.status'), dataIndex: 'status', width: 96, valueType: 'select',
        fieldProps: {
          allowClear: true,
          options: STATUS_VALUES.map((v) => ({ label: t(`workflow.status.${normalizeInstanceStatus(v)}`), value: v })),
        },
        render: (_, r) => (
          <Tag color={instanceStatusColor(r.status)}>{t(`workflow.status.${normalizeInstanceStatus(r.status)}`)}</Tag>
        ),
      },
      {
        title: t('workflow.monitor.createTime'), dataIndex: 'createTime', width: 170, search: false,
        render: (_, r) => formatDateTime(r.createTime),
      },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 100, fixed: 'right',
        render: (_, r) => (
          <Space size={0}>
            <Button type="link" size="small" onClick={(e) => { e.stopPropagation(); openDetail(r) }}>
              {t('workflow.monitor.view')}
            </Button>
          </Space>
        ),
      },
    ],
    [t, openDetail],
  )

  return (
    <DataTable<WfInstanceListItem>
      columns={columns}
      fetcher={fetchMonitor}
      persistKey="workflow-monitor"
      rowKey="id"
      onRowClick={openDetail}
    />
  )
}
