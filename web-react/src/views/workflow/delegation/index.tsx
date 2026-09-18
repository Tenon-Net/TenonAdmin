// 长期委托规则。菜单 component 填 `workflow/delegation/index`。
// 时间窗口是 UTC 绝对时刻(后端按 UTC 判定生效),表单收 dayjs、提交转 ISO。
// 新增/更新/删除都带 requestId 走后端 operation receipt 幂等:网络无定论时重试复用同一把键。
import { useCallback, useMemo, useRef, useState } from 'react'
import { App, Button, DatePicker, Form, Space, Switch, Tag } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useTranslation } from 'react-i18next'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import { Can } from '@/components/Can'
import { FormContainer } from '@/components/FormContainer'
import { UserSelect } from '@/components/UserSelect'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { wfDelegationApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import { classifyOutcome, useRequestKey } from '@/workflow/useRequestKey'
import { formatDateTime } from '@/workflow/statusLabels'
import type { WfDelegationRule } from '@/types/workflow'

interface DelegationFormValues {
  originalUserId?: number
  delegateUserId?: number
  enabled: boolean
  startsAt: Dayjs
  endsAt: Dayjs
}

const DEFAULT_WINDOW_DAYS = 30

export default function WfDelegationPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()

  const tableRef = useRef<DataTableHandle>(null)
  const reload = useCallback(() => tableRef.current?.reload(), [])

  // useRequestKey 是工厂(键存在闭包里)不是 hook:用 useRef 固定住首个实例,否则每次渲染都换一把新键。
  const requestKey = useRef(useRequestKey()).current
  // 删除的 requestId 按行缓存:删失败后重试要复用同一把键,成功后才丢弃。
  const deleteKeys = useRef(new Map<number, string>()).current

  const fetchRules: PageFetcher<WfDelegationRule> = (q) =>
    wfDelegationApi.page({
      page: q.page,
      pageSize: q.pageSize,
      originalUserId: typeof q.originalUserId === 'number' ? q.originalUserId : undefined,
      enabled: typeof q.enabled === 'boolean' ? q.enabled : undefined,
    })

  const [form] = Form.useForm<DelegationFormValues>()
  const [open, setOpen] = useState(false)
  const [editingId, setEditingId] = useState<number | null>(null)

  const openForm = useCallback(
    (row?: WfDelegationRule) => {
      setEditingId(row?.id == null ? null : Number(row.id))
      form.setFieldsValue({
        originalUserId: row?.originalUserId == null ? undefined : Number(row.originalUserId),
        delegateUserId: row?.delegateUserId == null ? undefined : Number(row.delegateUserId),
        enabled: row?.enabled ?? true,
        startsAt: row?.startsAt ? dayjs(row.startsAt) : dayjs(),
        endsAt: row?.endsAt ? dayjs(row.endsAt) : dayjs().add(DEFAULT_WINDOW_DAYS, 'day'),
      })
      requestKey.reset() // 打开一个新动作 → 换新键
      setOpen(true)
    },
    [form, requestKey],
  )

  const save = async () => {
    const v = await form.validateFields() // 校验失败抛 → FormContainer 不关
    try {
      const input = {
        originalUserId: v.originalUserId!,
        delegateUserId: v.delegateUserId!,
        enabled: v.enabled,
        startsAt: v.startsAt.toISOString(),
        endsAt: v.endsAt.toISOString(),
        requestId: requestKey.value(),
      }
      if (editingId === null) await wfDelegationApi.add(input)
      else await wfDelegationApi.update(editingId, input)
      requestKey.settle('success')
      message.success(t('common.success'))
      reload()
    } catch (e) {
      requestKey.settle(classifyOutcome(e))
      message.error(translateError(e))
      return false // 留在弹层,不关
    }
  }

  const handleDelete = useCallback(
    (row: WfDelegationRule) => {
      const id = Number(row.id)
      const requestId = deleteKeys.get(id) ?? `delete-${id}-${Date.now()}`
      deleteKeys.set(id, requestId)
      confirm({
        content: t('workflow.delegation.deleteConfirm'),
        action: () => wfDelegationApi.remove(id, requestId),
        successMsg: t('workflow.delegation.deleted'),
      }).then((ok) => {
        if (!ok) return
        deleteKeys.delete(id)
        reload()
      })
    },
    [confirm, deleteKeys, t, reload],
  )

  const columns = useMemo<ProColumns<WfDelegationRule>[]>(
    () => [
      {
        title: t('workflow.delegation.originalUser'), dataIndex: 'originalUserId', minWidth: 170,
        formItemRender: () => <UserSelect placeholder={t('workflow.delegation.originalUser')} />,
        render: (_, r) => r.originalUserName || `#${r.originalUserId}`,
      },
      {
        title: t('workflow.delegation.delegateUser'), dataIndex: 'delegateUserId', minWidth: 170, search: false,
        render: (_, r) => r.delegateUserName || `#${r.delegateUserId}`,
      },
      {
        title: t('workflow.delegation.startsAt'), dataIndex: 'startsAt', width: 190, search: false,
        render: (_, r) => formatDateTime(r.startsAt),
      },
      {
        title: t('workflow.delegation.endsAt'), dataIndex: 'endsAt', width: 190, search: false,
        render: (_, r) => formatDateTime(r.endsAt),
      },
      {
        title: t('workflow.delegation.enabled'), dataIndex: 'enabled', width: 110, valueType: 'select',
        fieldProps: {
          allowClear: true,
          options: [
            { label: t('workflow.delegation.active'), value: true },
            { label: t('workflow.delegation.disabled'), value: false },
          ],
        },
        render: (_, r) => (
          <Tag color={r.enabled ? 'success' : 'default'}>
            {t(r.enabled ? 'workflow.delegation.active' : 'workflow.delegation.disabled')}
          </Tag>
        ),
      },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 150, fixed: 'right',
        render: (_, r) => (
          <Space size={0}>
            {has('PUT:/api/v1/workflow/delegation/{id}') && (
              <Button type="link" size="small" onClick={() => openForm(r)}>{t('common.edit')}</Button>
            )}
            {has('DELETE:/api/v1/workflow/delegation/{id}') && (
              <Button type="link" size="small" danger onClick={() => handleDelete(r)}>{t('common.delete')}</Button>
            )}
          </Space>
        ),
      },
    ],
    [t, has, openForm, handleDelete],
  )

  return (
    <>
      <DataTable<WfDelegationRule>
        ref={tableRef}
        columns={columns}
        fetcher={fetchRules}
        persistKey="workflow-delegation"
        rowKey="id"
        toolbar={
          <Can code="POST:/api/v1/workflow/delegation/add">
            <Button type="primary" onClick={() => openForm()}>{t('workflow.delegation.add')}</Button>
          </Can>
        }
      />

      <FormContainer
        open={open}
        onOpenChange={setOpen}
        title={editingId === null ? t('workflow.delegation.add') : t('workflow.delegation.edit')}
        width={520}
        confirmText={t('workflow.delegation.save')}
        onConfirm={save}
      >
        <Form form={form} labelCol={{ span: 7 }} wrapperCol={{ span: 17 }} style={{ marginTop: 12 }}>
          <Form.Item
            name="originalUserId" label={t('workflow.delegation.originalUser')}
            rules={[{ required: true, message: t('workflow.delegation.required') }]}
          >
            {/* 原责任人建后不可改(改了就是另一条规则) */}
            <UserSelect disabled={editingId !== null} placeholder={t('workflow.delegation.originalUser')} />
          </Form.Item>
          <Form.Item
            name="delegateUserId" label={t('workflow.delegation.delegateUser')}
            rules={[{ required: true, message: t('workflow.delegation.required') }]}
          >
            <UserSelect placeholder={t('workflow.delegation.delegateUser')} />
          </Form.Item>
          <Form.Item
            name="startsAt" label={t('workflow.delegation.startsAt')}
            rules={[{ required: true, message: t('workflow.delegation.required') }]}
          >
            <DatePicker showTime style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item
            name="endsAt" label={t('workflow.delegation.endsAt')}
            rules={[
              { required: true, message: t('workflow.delegation.required') },
              {
                // 空窗口/倒挂窗口后端也拒,这里先拦一道给出同一句提示。
                validator: (_, value: Dayjs | null) => {
                  const startsAt = form.getFieldValue('startsAt') as Dayjs | undefined
                  return value && startsAt && !value.isAfter(startsAt)
                    ? Promise.reject(new Error(t('workflow.delegation.required')))
                    : Promise.resolve()
                },
              },
            ]}
          >
            <DatePicker showTime style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="enabled" label={t('workflow.delegation.enabled')} valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </FormContainer>
    </>
  )
}
