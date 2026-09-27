// 我已办的。菜单 component 填 `workflow/done/index`。
// 行点击或「查看」进实例详情;动作/实例状态标签走 `workflow/statusLabels`(与详情页同一套数字表)。
import { useCallback, useMemo } from 'react'
import { Button, Space, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type PageFetcher } from '@/components/DataTable'
import { wfTaskApi } from '@/api/workflow'
import {
  formatDateTime,
  instanceStatusColor,
  normalizeInstanceStatus,
  normalizeTaskAction,
  taskActionColor,
} from '@/workflow/statusLabels'
import type { WfDoneItem } from '@/types/workflow'

export default function WfDonePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()

  const fetchDone: PageFetcher<WfDoneItem> = (q) => wfTaskApi.done({ page: q.page, pageSize: q.pageSize })

  const openDetail = useCallback(
    (r: WfDoneItem) => {
      if (r.instanceId == null) return
      navigate(`/workflow/instance/${r.instanceId}/detail`)
    },
    [navigate],
  )

  const columns = useMemo<ProColumns<WfDoneItem>[]>(
    () => [
      { title: t('workflow.done.definition'), dataIndex: 'definitionName', minWidth: 160, ellipsis: true, search: false },
      {
        title: t('workflow.done.node'), dataIndex: 'nodeName', width: 140, ellipsis: true, search: false,
        render: (_, r) => r.nodeName || r.nodeId,
      },
      {
        title: t('workflow.done.action'), dataIndex: 'action', width: 96, search: false,
        render: (_, r) => (
          <Tag color={taskActionColor(r.action)}>{t(`workflow.action.${normalizeTaskAction(r.action)}`)}</Tag>
        ),
      },
      {
        title: t('workflow.done.businessKey'), dataIndex: 'businessKey', width: 140, ellipsis: true, search: false,
        render: (_, r) => r.businessKey || '—',
      },
      {
        title: t('common.status'), dataIndex: 'instanceStatus', width: 96, search: false,
        render: (_, r) => (
          <Tag color={instanceStatusColor(r.instanceStatus)}>{t(`workflow.status.${normalizeInstanceStatus(r.instanceStatus)}`)}</Tag>
        ),
      },
      {
        title: t('workflow.done.createTime'), dataIndex: 'createTime', width: 170, search: false,
        render: (_, r) => formatDateTime(r.createTime),
      },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 100, fixed: 'right',
        render: (_, r) => (
          <Space size={0}>
            {/* 行本身可点进详情,按钮须 stopPropagation 否则冒泡再触发一次 */}
            <Button type="link" size="small" onClick={(e) => { e.stopPropagation(); openDetail(r) }}>
              {t('workflow.done.view')}
            </Button>
          </Space>
        ),
      },
    ],
    [t, openDetail],
  )

  return (
    <DataTable<WfDoneItem>
      columns={columns}
      fetcher={fetchDone}
      persistKey="workflow-done"
      rowKey="hisTaskId"
      search={false}
      onRowClick={openDetail}
    />
  )
}
