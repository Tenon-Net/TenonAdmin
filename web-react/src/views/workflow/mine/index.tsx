// 我发起的。菜单 component 填 `workflow/mine/index`。
// 行点击或「查看」进实例详情。
import { useCallback, useMemo } from 'react'
import { Button, Space, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type PageFetcher } from '@/components/DataTable'
import { wfInstanceApi } from '@/api/workflow'
import { formatDateTime, instanceStatusColor, normalizeInstanceStatus } from '@/workflow/statusLabels'
import type { WfInstanceListItem } from '@/types/workflow'

export default function WfMinePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()

  const fetchMine: PageFetcher<WfInstanceListItem> = (q) => wfInstanceApi.page({ page: q.page, pageSize: q.pageSize })

  const openDetail = useCallback(
    (r: WfInstanceListItem) => {
      if (r.id == null) return
      navigate(`/workflow/instance/${r.id}/detail`)
    },
    [navigate],
  )

  const columns = useMemo<ProColumns<WfInstanceListItem>[]>(
    () => [
      { title: t('workflow.mine.definition'), dataIndex: 'definitionName', minWidth: 160, ellipsis: true, search: false },
      {
        title: t('workflow.mine.version'), dataIndex: 'version', width: 80, align: 'center', search: false,
        render: (_, r) => (r.version == null ? '—' : `v${r.version}`),
      },
      {
        title: t('workflow.mine.businessKey'), dataIndex: 'businessKey', width: 140, ellipsis: true, search: false,
        render: (_, r) => r.businessKey || '—',
      },
      {
        title: t('common.status'), dataIndex: 'status', width: 96, search: false,
        render: (_, r) => (
          <Tag color={instanceStatusColor(r.status)}>{t(`workflow.status.${normalizeInstanceStatus(r.status)}`)}</Tag>
        ),
      },
      {
        title: t('workflow.mine.createTime'), dataIndex: 'createTime', width: 170, search: false,
        render: (_, r) => formatDateTime(r.createTime),
      },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 100, fixed: 'right',
        render: (_, r) => (
          <Space size={0}>
            <Button type="link" size="small" onClick={(e) => { e.stopPropagation(); openDetail(r) }}>
              {t('workflow.mine.view')}
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
      fetcher={fetchMine}
      persistKey="workflow-mine"
      rowKey="id"
      search={false}
      onRowClick={openDetail}
    />
  )
}
