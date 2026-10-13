// 流程监控。菜单 component 填 `workflow/monitor/index`。
// 参与筛选(发起人/办理人/抄送人)是业务过滤,不是数据权限;行点击进同一实例详情。
// 运行中的实例可以强制转办、终止或跳过阻断节点,按钮各自对应一条权限码。
import { useCallback, useMemo, useRef, useState } from 'react'
import { App, Button, Form, Input, Modal, Space, Tag } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import { UserSelect } from '@/components/UserSelect'
import { wfInstanceApi } from '@/api/workflow'
import { useHasPerm } from '@/stores/auth'
import { normalizeWfId } from '@/workflow/id'
import { formatDateTime, instanceStatusColor, normalizeInstanceStatus } from '@/workflow/statusLabels'
import { translateError } from '@/utils/error'
import type { WfInstanceListItem } from '@/types/workflow'

/** 实例状态 1–5(与 statusLabels 的数字表同源)。 */
const STATUS_VALUES = [1, 2, 3, 4, 5] as const
const ADMIN_PERM = {
  transfer: 'POST:/api/v1/workflow/instance/force-transfer',
  terminate: 'POST:/api/v1/workflow/instance/terminate',
  skip: 'POST:/api/v1/workflow/instance/skip',
} as const
type AdminKind = keyof typeof ADMIN_PERM

export default function WfMonitorPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { message } = App.useApp()
  const has = useHasPerm()
  const tableRef = useRef<DataTableHandle>(null)
  const [kind, setKind] = useState<AdminKind | null>(null)
  const [row, setRow] = useState<WfInstanceListItem | null>(null)
  const [reason, setReason] = useState('')
  const [toUserId, setToUserId] = useState<number | string | null>(null)
  const [nodeId, setNodeId] = useState('')
  const [submitting, setSubmitting] = useState(false)

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

  const openAdmin = useCallback((next: AdminKind, target: WfInstanceListItem) => {
    setKind(next)
    setRow(target)
    setReason('')
    setToUserId(null)
    setNodeId('')
  }, [])

  const closeAdmin = useCallback(() => {
    if (submitting) return
    setKind(null)
  }, [submitting])

  const submitAdmin = useCallback(async () => {
    if (row?.id == null || kind == null) return
    const text = reason.trim()
    if (!text) {
      message.warning(t('workflow.monitor.reasonRequired'))
      return
    }
    const userId = normalizeWfId(toUserId)
    if (kind === 'transfer' && userId == null) {
      message.warning(t('workflow.monitor.admin.issue.targetRequired'))
      return
    }
    setSubmitting(true)
    try {
      const instanceId = Number(row.id)
      const node = nodeId.trim() || null
      if (kind === 'transfer') {
        await wfInstanceApi.forceTransfer({ instanceId, toUserId: Number(userId), reason: text, nodeId: node })
      } else if (kind === 'terminate') {
        await wfInstanceApi.terminate({ instanceId, reason: text })
      } else {
        await wfInstanceApi.skip({ instanceId, reason: text, nodeId: node })
      }
      message.success(t('common.success'))
      setKind(null)
      tableRef.current?.reload()
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setSubmitting(false)
    }
  }, [kind, message, nodeId, reason, row, t, toUserId])

  const columns = useMemo<ProColumns<WfInstanceListItem>[]>(
    () => [
      { title: t('workflow.monitor.definition'), dataIndex: 'definitionName', minWidth: 160, ellipsis: true, search: false },
      {
        title: t('workflow.monitor.version'), dataIndex: 'version', width: 80, align: 'center', search: false,
        render: (_, r) => (r.version == null ? '—' : `v${r.version}`),
      },
      {
        title: t('workflow.monitor.starter'), dataIndex: 'starterUserId', width: 140,
        formItemRender: () => <UserSelect source="workflow" placeholder={t('workflow.monitor.starter')} />,
        render: (_, r) => (r.starterUserId == null ? '—' : t('workflow.detail.userFallback', { id: r.starterUserId })),
      },
      {
        title: t('workflow.monitor.actor'), dataIndex: 'actorUserId', hideInTable: true, hideInSetting: true,
        formItemRender: () => <UserSelect source="workflow" placeholder={t('workflow.monitor.actor')} />,
      },
      {
        title: t('workflow.monitor.cc'), dataIndex: 'ccUserId', hideInTable: true, hideInSetting: true,
        formItemRender: () => <UserSelect source="workflow" placeholder={t('workflow.monitor.cc')} />,
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
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 280, fixed: 'right',
        render: (_, r) => (
          <Space size={0} wrap>
            <Button type="link" size="small" onClick={(e) => { e.stopPropagation(); openDetail(r) }}>
              {t('workflow.monitor.view')}
            </Button>
            {Number(r.status) === 1 ? (['transfer', 'terminate', 'skip'] as const).map((item) => (
              has(ADMIN_PERM[item]) ? (
                <Button
                  key={item}
                  type="link"
                  size="small"
                  danger={item === 'terminate'}
                  onClick={(e) => { e.stopPropagation(); openAdmin(item, r) }}
                >
                  {t(`workflow.monitor.${item === 'transfer' ? 'forceTransfer' : item}`)}
                </Button>
              ) : null
            )) : null}
          </Space>
        ),
      },
    ],
    [t, openDetail, openAdmin, has],
  )

  const titleKey = kind === 'transfer' ? 'forceTransfer' : kind

  return (
    <>
      <DataTable<WfInstanceListItem>
        ref={tableRef}
        columns={columns}
        fetcher={fetchMonitor}
        persistKey="workflow-monitor"
        rowKey="id"
        onRowClick={openDetail}
      />
      <Modal
        open={kind != null}
        title={kind ? t(`workflow.monitor.${titleKey}`) : ''}
        confirmLoading={submitting}
        okText={t('common.confirm')}
        cancelText={t('common.cancel')}
        onCancel={closeAdmin}
        onOk={() => { void submitAdmin() }}
        destroyOnHidden
      >
        <Form layout="vertical">
          {kind === 'transfer' ? (
            <Form.Item label={t('workflow.monitor.targetUser')} required>
              <UserSelect source="workflow"
                value={toUserId ?? undefined}
                placeholder={t('workflow.monitor.targetUser')}
                onChange={(value) => setToUserId(value as number | string | null)}
              />
            </Form.Item>
          ) : null}
          {kind !== 'terminate' ? (
            <Form.Item label={t('workflow.monitor.nodeId')}>
              <Input value={nodeId} allowClear onChange={(e) => setNodeId(e.target.value)} />
            </Form.Item>
          ) : null}
          <Form.Item label={t('workflow.monitor.reason')} required>
            <Input.TextArea value={reason} maxLength={512} showCount rows={3} onChange={(e) => setReason(e.target.value)} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}
