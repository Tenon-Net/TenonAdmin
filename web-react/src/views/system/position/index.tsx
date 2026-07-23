// 岗位管理页。标准列表 CRUD:搜索(名称)+ 工具栏 + 表格 + 行内启停 + 新增/编辑弹窗。
// 岗位与机构无关联;后端无独立启停端点 → 行内启停走全量 update(同 module 页),编辑时 code 禁改。
// 照 B11 user 页范式:纯逻辑抽 positionForm.ts(变异钉),本页只接线。
import { useCallback, useMemo, useRef, useState } from 'react'
import { App, Button, Form, Input, InputNumber, Space, Switch } from 'antd'
import { useTranslation } from 'react-i18next'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import type { ProColumns } from '@ant-design/pro-components'
import { Can } from '@/components/Can'
import { FormContainer } from '@/components/FormContainer'
import { StatusSwitch } from '@/components/StatusSwitch'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { positionApi } from '@/api'
import { translateError } from '@/utils/error'
import type { PositionInput, SysPosition } from '@/types/api'
import { blankForm, rowToInput } from './positionForm'

export default function PositionPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()

  const tableRef = useRef<DataTableHandle>(null)
  const reload = useCallback(() => tableRef.current?.reload(), [])

  // 分页取数:ProTable 搜索表单值(unknown)→ positionApi.page 强类型入参。name 来自搜索列;
  // 非字符串搜索值滤成 undefined(不把脏参透传后端)。有意不 memo(同 user 页,ProTable 经 ref 读 request)。
  const fetchPositions: PageFetcher<SysPosition> = (q) =>
    positionApi.page({
      page: q.page,
      pageSize: q.pageSize,
      name: typeof q.name === 'string' ? q.name : undefined,
      sortField: q.sortField,
      sortOrder: q.sortOrder,
    })

  // ── 新增/编辑弹窗(FormContainer owns loading+close)──
  const [form] = Form.useForm<PositionInput>()
  const [open, setOpen] = useState(false)
  const [editingId, setEditingId] = useState<number | null>(null)

  const openAdd = () => {
    setEditingId(null)
    form.setFieldsValue(blankForm())
    setOpen(true)
  }
  const openEdit = useCallback(
    (r: SysPosition) => {
      setEditingId(r.id)
      form.setFieldsValue(rowToInput(r))
      setOpen(true)
    },
    [form],
  )

  const save = async () => {
    const v = await form.validateFields() // 校验失败抛 → FormContainer 不关
    try {
      if (editingId === null) await positionApi.add(v)
      else await positionApi.update(editingId, v)
      message.success(t('position.saved'))
      reload()
    } catch (e) {
      message.error(translateError(e))
      return false // 留在弹层,不关
    }
  }

  const handleDelete = useCallback(
    (r: SysPosition) => {
      confirm({ content: t('position.deleteConfirm', { name: r.name }), action: () => positionApi.remove(r.id), successMsg: t('position.deleted') }).then(
        (ok) => { if (ok) reload() },
      )
    },
    [confirm, t, reload],
  )

  const columns = useMemo<ProColumns<SysPosition>[]>(
    () => [
      { title: t('position.name'), dataIndex: 'name' },
      { title: t('position.code'), dataIndex: 'code', search: false },
      { title: t('position.sort'), dataIndex: 'sort', search: false, width: 90 },
      {
        title: t('common.status'), dataIndex: 'enabled', search: false, width: 90,
        render: (_, r) => (
          <StatusSwitch
            value={r.enabled}
            disabled={!has('PUT:/api/v1/sys/position/{id}')}
            request={(next) => positionApi.update(r.id, { ...rowToInput(r), enabled: next })}
            onChange={reload}
          />
        ),
      },
      { title: t('common.createTime'), dataIndex: 'createTime', search: false },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 140, fixed: 'right',
        render: (_, r) => (
          <Space size={4}>
            {has('PUT:/api/v1/sys/position/{id}') && <Button type="link" size="small" onClick={() => openEdit(r)}>{t('common.edit')}</Button>}
            {has('DELETE:/api/v1/sys/position/{id}') && <Button type="link" size="small" danger onClick={() => handleDelete(r)}>{t('common.delete')}</Button>}
          </Space>
        ),
      },
    ],
    [t, has, reload, openEdit, handleDelete],
  )

  return (
    <>
      <DataTable<SysPosition>
        ref={tableRef}
        columns={columns}
        fetcher={fetchPositions}
        persistKey="sys-position"
        toolbar={
          <Can code="POST:/api/v1/sys/position/add">
            <Button type="primary" onClick={openAdd}>{t('common.add')}</Button>
          </Can>
        }
      />

      <FormContainer
        open={open}
        onOpenChange={setOpen}
        title={editingId === null ? t('position.addTitle') : t('position.editTitle')}
        width={480}
        confirmText={t('common.save')}
        onConfirm={save}
      >
        <Form form={form} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }} style={{ marginTop: 12 }}>
          <Form.Item
            name="name" label={t('position.name')}
            rules={[{ required: true, whitespace: true, message: t('position.nameRequired') }]}
          >
            <Input placeholder={t('position.name')} />
          </Form.Item>
          <Form.Item name="code" label={t('position.code')}>
            {/* 岗位编码建后不可改(后端也拒);编辑时置灰 */}
            <Input disabled={editingId !== null} placeholder={t('position.codePlaceholder')} />
          </Form.Item>
          <Form.Item name="sort" label={t('position.sort')}>
            <InputNumber min={0} style={{ width: 160 }} />
          </Form.Item>
          <Form.Item name="enabled" label={t('common.status')} valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </FormContainer>
    </>
  )
}
