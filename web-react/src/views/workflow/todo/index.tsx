// 待我审批列表。菜单 component 填 `workflow/todo/index`。
// 「办理」进实例详情(`/workflow/instance/:id/detail`);逾期(dueTime 已过)标警示 Tag。
import { useCallback, useMemo } from 'react'
import { Button, Space, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type PageFetcher } from '@/components/DataTable'
import { wfTaskApi } from '@/api/workflow'
import { formatDateTime } from '@/workflow/statusLabels'
import type { WfTodoItem } from '@/types/workflow'

function isOverdue(due: string | null | undefined): boolean {
  if (!due) return false
  const ts = Date.parse(due)
  return Number.isFinite(ts) && ts < Date.now()
}

export default function WfTodoPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()

  const fetchTodos: PageFetcher<WfTodoItem> = (q) => wfTaskApi.todo({ page: q.page, pageSize: q.pageSize })

  const openDetail = useCallback(
    (r: WfTodoItem) => navigate(`/workflow/instance/${r.instanceId}/detail`),
    [navigate],
  )

  const columns = useMemo<ProColumns<WfTodoItem>[]>(
    () => [
      { title: t('workflow.todo.definition'), dataIndex: 'definitionName', minWidth: 160, ellipsis: true, search: false },
      {
        title: t('workflow.todo.node'), dataIndex: 'nodeName', width: 140, ellipsis: true, search: false,
        render: (_, r) => r.nodeName || r.nodeId,
      },
      {
        title: t('common.status'), key: 'status', width: 96, search: false,
        render: () => <Tag color="processing">{t('workflow.todo.pending')}</Tag>,
      },
      {
        title: t('workflow.todo.businessKey'), dataIndex: 'businessKey', width: 140, ellipsis: true, search: false,
        render: (_, r) => r.businessKey || '—',
      },
      {
        title: t('workflow.todo.createTime'), dataIndex: 'createTime', width: 170, search: false,
        render: (_, r) => formatDateTime(r.createTime),
      },
      {
        title: t('workflow.todo.dueTime'), dataIndex: 'dueTime', width: 170, search: false,
        render: (_, r) => {
          if (!r.dueTime) return '—'
          const text = formatDateTime(r.dueTime)
          return isOverdue(r.dueTime) ? <Tag color="warning">{`${t('workflow.todo.overdue')} ${text}`}</Tag> : text
        },
      },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 100, fixed: 'right',
        render: (_, r) => (
          <Space size={0}>
            <Button type="link" size="small" onClick={() => openDetail(r)}>{t('workflow.todo.handle')}</Button>
          </Space>
        ),
      },
    ],
    [t, openDetail],
  )

  return (
    <DataTable<WfTodoItem>
      columns={columns}
      fetcher={fetchTodos}
      persistKey="workflow-todo"
      rowKey="taskId"
      search={false}
    />
  )
}
