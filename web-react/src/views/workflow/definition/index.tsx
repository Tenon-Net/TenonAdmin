// 流程定义列表。菜单 component 填 `workflow/definition/index`。
// 「新建」与「设计」都跳设计器(`/workflow/definition/designer`,可带 `?id=`)—— 建草稿的逻辑留在设计器空态,本页不重复一份。
import { useCallback, useMemo, useRef } from 'react'
import { Button, Dropdown, Space, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import { AppIcon } from '@/components/AppIcon'
import { Can } from '@/components/Can'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { wfDefinitionApi } from '@/api/workflow'
import { DEF_STATUS, formatDateTime } from '@/workflow/statusLabels'
import type { WfDefinitionRow } from '@/types/workflow'

/** 后端 WfDefinitionStatus:0 草稿 / 1 已发布 / 2 停用(色与 Vue 侧 tagType 一致)。 */
const STATUS_TAG: Record<number, { color: string; key: string }> = {
  [DEF_STATUS.DRAFT]: { color: 'default', key: 'workflow.definition.status.draft' },
  [DEF_STATUS.PUBLISHED]: { color: 'success', key: 'workflow.definition.status.published' },
  [DEF_STATUS.DISABLED]: { color: 'warning', key: 'workflow.definition.status.disabled' },
}

export default function WfDefinitionPage() {
  const { t } = useTranslation()
  const { confirm } = useConfirm()
  const has = useHasPerm()
  const navigate = useNavigate()

  const tableRef = useRef<DataTableHandle>(null)
  const reload = useCallback(() => tableRef.current?.reload(), [])

  // 有意不 memo(同 position / notice 页,ProTable 经 ref 读 request)。
  const fetchDefinitions: PageFetcher<WfDefinitionRow> = (q) =>
    wfDefinitionApi.page({
      page: q.page,
      pageSize: q.pageSize,
      name: typeof q.name === 'string' ? q.name : undefined,
      groupName: typeof q.groupName === 'string' ? q.groupName : undefined,
      status: typeof q.status === 'number' ? q.status : undefined,
    })

  const openDesigner = useCallback(
    (r?: WfDefinitionRow) => {
      navigate(r?.id == null ? '/workflow/definition/designer' : `/workflow/definition/designer?id=${r.id}`)
    },
    [navigate],
  )

  const handlePublish = useCallback(
    (r: WfDefinitionRow) => {
      confirm({
        content: t('workflow.definition.publishConfirm', { name: r.name ?? '' }),
        action: () => wfDefinitionApi.publish(Number(r.id)),
        successMsg: t('workflow.definition.publishOk'),
      }).then((ok) => { if (ok) reload() })
    },
    [confirm, t, reload],
  )

  const handleDisable = useCallback(
    (r: WfDefinitionRow) => {
      confirm({
        content: t('workflow.definition.disableConfirm', { name: r.name ?? '' }),
        action: () => wfDefinitionApi.disable(Number(r.id)),
        successMsg: t('workflow.definition.disableOk'),
      }).then((ok) => { if (ok) reload() })
    },
    [confirm, t, reload],
  )

  const handleDelete = useCallback(
    (r: WfDefinitionRow) => {
      confirm({
        content: t('workflow.definition.deleteConfirm', { name: r.name ?? '' }),
        action: () => wfDefinitionApi.remove(Number(r.id)),
      }).then((ok) => { if (ok) reload() })
    },
    [confirm, t, reload],
  )

  const columns = useMemo<ProColumns<WfDefinitionRow>[]>(
    () => [
      {
        title: t('workflow.definition.name'), dataIndex: 'name', ellipsis: true, minWidth: 180,
        render: (_, r) => (
          <Space size={6}>
            {r.icon ? <AppIcon icon={r.icon} size={18} /> : null}
            {r.name || '—'}
          </Space>
        ),
      },
      {
        title: t('workflow.definition.group'), dataIndex: 'groupName', width: 140, ellipsis: true,
        render: (_, r) => r.groupName || '—',
      },
      {
        title: t('common.status'), dataIndex: 'status', width: 110, valueType: 'select',
        fieldProps: {
          allowClear: true,
          options: Object.entries(STATUS_TAG).map(([v, s]) => ({ label: t(s.key), value: Number(v) })),
        },
        render: (_, r) => {
          const s = STATUS_TAG[Number(r.status)]
          return s ? <Tag color={s.color}>{t(s.key)}</Tag> : '—'
        },
      },
      {
        title: t('workflow.definition.version'), dataIndex: 'currentVersion', width: 100, align: 'center', search: false,
        // 草稿的 currentVersion 是 0:显示 v0 会被当成「有个 0 版」,直接给「未发布」更实。
        render: (_, r) =>
          Number(r.currentVersion) >= 1
            ? `v${r.currentVersion}`
            : <span style={{ color: 'var(--color-text-tertiary)' }}>{t('workflow.definition.unpublished')}</span>,
      },
      {
        title: t('common.createTime'), dataIndex: 'createTime', width: 170, search: false,
        render: (_, r) => formatDateTime(r.createTime),
      },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 150, fixed: 'right',
        render: (_, r) => {
          // 发布/停用按各自权限码显隐,且按当前状态取舍:已停用不再给「停用」。
          const items = [
            has('POST:/api/v1/workflow/definition/publish')
              ? { key: 'publish', label: t('workflow.definition.publish') }
              : null,
            has('POST:/api/v1/workflow/definition/disable') && Number(r.status) !== DEF_STATUS.DISABLED
              ? { key: 'disable', label: t('workflow.definition.disable') }
              : null,
            has('DELETE:/api/v1/workflow/definition/{id}')
              ? { key: 'delete', label: t('common.delete'), danger: true }
              : null,
          ].filter((o): o is { key: string; label: string; danger?: boolean } => o !== null)

          return (
            <Space size={0}>
              {has('GET:/api/v1/workflow/definition/{id}') && (
                <Button type="link" size="small" onClick={() => openDesigner(r)}>{t('workflow.definition.design')}</Button>
              )}
              {items.length > 0 && (
                <Dropdown
                  trigger={['click']}
                  menu={{
                    items,
                    onClick: ({ key }) => {
                      if (key === 'publish') handlePublish(r)
                      else if (key === 'disable') handleDisable(r)
                      else handleDelete(r)
                    },
                  }}
                >
                  <Button type="link" size="small">{t('common.more')}</Button>
                </Dropdown>
              )}
            </Space>
          )
        },
      },
    ],
    [t, has, openDesigner, handlePublish, handleDisable, handleDelete],
  )

  return (
    <DataTable<WfDefinitionRow>
      ref={tableRef}
      columns={columns}
      fetcher={fetchDefinitions}
      persistKey="workflow-definition"
      rowKey="id"
      toolbar={
        <Can code="POST:/api/v1/workflow/definition/add">
          <Button type="primary" icon={<AppIcon icon="ph:plus" size={16} />} onClick={() => openDesigner()}>
            {t('workflow.definition.create')}
          </Button>
        </Can>
      }
    />
  )
}
