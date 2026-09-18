// 抄送我的。菜单 component 填 `workflow/cc/index`。
// 「查看」进实例详情;详情 GET 会把该用户本实例的未读行标已读。
import { useCallback, useMemo } from 'react'
import { Button, Space, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type PageFetcher } from '@/components/DataTable'
import { wfCcApi } from '@/api/workflow'
import { formatDateTime } from '@/workflow/statusLabels'
import type { WfCcItem } from '@/types/workflow'

export default function WfCcPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()

  const fetchCc: PageFetcher<WfCcItem> = (q) => wfCcApi.page({ page: q.page, pageSize: q.pageSize })

  const openDetail = useCallback(
    (r: WfCcItem) => navigate(`/workflow/instance/${r.instanceId}/detail`),
    [navigate],
  )

  const columns = useMemo<ProColumns<WfCcItem>[]>(
    () => [
      { title: t('workflow.cc.definition'), dataIndex: 'definitionName', minWidth: 160, ellipsis: true, search: false },
      {
        title: t('workflow.cc.node'), dataIndex: 'nodeName', width: 140, ellipsis: true, search: false,
        render: (_, r) => r.nodeName || r.nodeId,
      },
      {
        title: t('workflow.cc.readState'), dataIndex: 'isRead', width: 88, search: false,
        render: (_, r) => (
          <Tag color={r.isRead ? 'default' : 'processing'}>{t(r.isRead ? 'workflow.cc.read' : 'workflow.cc.unread')}</Tag>
        ),
      },
      {
        title: t('workflow.cc.businessKey'), dataIndex: 'businessKey', width: 140, ellipsis: true, search: false,
        render: (_, r) => r.businessKey || '—',
      },
      {
        title: t('workflow.cc.createTime'), dataIndex: 'createTime', width: 170, search: false,
        render: (_, r) => formatDateTime(r.createTime),
      },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 100, fixed: 'right',
        render: (_, r) => (
          <Space size={0}>
            <Button type="link" size="small" onClick={() => openDetail(r)}>{t('workflow.cc.view')}</Button>
          </Space>
        ),
      },
    ],
    [t, openDetail],
  )

  return (
    <DataTable<WfCcItem>
      columns={columns}
      fetcher={fetchCc}
      persistKey="workflow-cc"
      rowKey="id"
      search={false}
    />
  )
}
