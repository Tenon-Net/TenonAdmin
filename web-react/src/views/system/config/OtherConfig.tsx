// 其他配置 = 通用兜底:仅列非预置分组(排除 sys/security/upload)的自定义 key-value,避免与结构化 Tab 重复。
// 列驱动搜索/分页/竞态交 DataTable;新增/编辑弹窗手写(与 module 页一致)。表单渲染全部字段 → validateFields 即全量(无 C9 漏字段)。
import { useCallback, useMemo, useRef, useState } from 'react'
import { App, Button, Form, Input, InputNumber, Space } from 'antd'
import { useTranslation } from 'react-i18next'
import type { ProColumns } from '@ant-design/pro-components'
import { DataTable, type DataTableHandle, type PageFetcher } from '@/components/DataTable'
import { Can } from '@/components/Can'
import { FormContainer } from '@/components/FormContainer'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { configApi } from '@/api'
import { translateError } from '@/utils/error'
import type { ConfigInput, SysConfig } from '@/types/api'
import { STRUCTURED_GROUPS, blankConfig, configToInput } from './configForm'

export default function OtherConfig() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()
  const tableRef = useRef<DataTableHandle>(null)
  const reload = useCallback(() => tableRef.current?.reload(), [])

  const fetcher: PageFetcher<SysConfig> = (q) =>
    configApi.page({
      page: q.page,
      pageSize: q.pageSize,
      configKey: typeof q.configKey === 'string' ? q.configKey : undefined,
      name: typeof q.name === 'string' ? q.name : undefined,
      groupCode: typeof q.groupCode === 'string' ? q.groupCode : undefined,
      excludedGroupCodes: STRUCTURED_GROUPS,
    })

  const askDelete = useCallback(
    (r: SysConfig) => {
      confirm({ content: t('config.deleteConfirm', { name: r.name }), action: () => configApi.remove(r.id), successMsg: t('config.deleted') }).then(
        (ok) => { if (ok) reload() },
      )
    },
    [confirm, t, reload],
  )

  // ── 新增/编辑弹窗 ──
  const [form] = Form.useForm<ConfigInput>()
  const [open, setOpen] = useState(false)
  const [editingId, setEditingId] = useState<number | null>(null)
  const openAdd = useCallback(() => { setEditingId(null); form.setFieldsValue(blankConfig()); setOpen(true) }, [form])
  const openEdit = useCallback((r: SysConfig) => { setEditingId(r.id); form.setFieldsValue(configToInput(r)); setOpen(true) }, [form])
  const save = async () => {
    const v = await form.validateFields()
    try {
      if (editingId === null) await configApi.add(v)
      else await configApi.update(editingId, v)
      message.success(t('config.saved'))
      reload()
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  const columns = useMemo<ProColumns<SysConfig>[]>(
    () => [
      { title: t('config.key'), dataIndex: 'configKey', ellipsis: true },
      { title: t('config.name'), dataIndex: 'name' },
      { title: t('config.value'), dataIndex: 'configValue', search: false, ellipsis: true, render: (_, r) => r.configValue || '—' },
      { title: t('config.group'), dataIndex: 'groupCode', render: (_, r) => r.groupCode || '—' },
      { title: t('config.sort'), dataIndex: 'sort', search: false, width: 80 },
      { title: t('common.createTime'), dataIndex: 'createTime', search: false, width: 170 },
      {
        title: t('common.operation'), key: 'op', search: false, hideInSetting: true, width: 140,
        render: (_, r) => (
          <Space size={4}>
            {has('PUT:/api/v1/sys/config/{id}') && <Button type="link" size="small" onClick={() => openEdit(r)}>{t('common.edit')}</Button>}
            {has('DELETE:/api/v1/sys/config/{id}') && <Button type="link" size="small" danger onClick={() => askDelete(r)}>{t('common.delete')}</Button>}
          </Space>
        ),
      },
    ],
    [t, has, openEdit, askDelete],
  )

  return (
    <>
      <DataTable<SysConfig>
        ref={tableRef}
        columns={columns}
        fetcher={fetcher}
        persistKey="sys-config"
        toolbar={<Can code="POST:/api/v1/sys/config"><Button type="primary" onClick={openAdd}>{t('common.add')}</Button></Can>}
      />
      <FormContainer
        open={open}
        onOpenChange={setOpen}
        title={editingId === null ? t('config.addTitle') : t('config.editTitle')}
        width={520}
        confirmText={t('common.save')}
        onConfirm={save}
      >
        <Form form={form} labelCol={{ span: 5 }} wrapperCol={{ span: 19 }} style={{ marginTop: 12 }}>
          <Form.Item name="configKey" label={t('config.key')} rules={[{ required: true, whitespace: true, message: t('config.keyRequired') }]}>
            <Input disabled={editingId !== null} placeholder={t('config.key')} />
          </Form.Item>
          <Form.Item name="name" label={t('config.name')} rules={[{ required: true, whitespace: true, message: t('config.nameRequired') }]}>
            <Input placeholder={t('config.name')} />
          </Form.Item>
          <Form.Item name="configValue" label={t('config.value')}>
            <Input.TextArea autoSize={{ minRows: 2 }} />
          </Form.Item>
          <Form.Item name="groupCode" label={t('config.group')}>
            <Input placeholder={t('config.group')} />
          </Form.Item>
          <Form.Item name="sort" label={t('config.sort')}>
            <InputNumber min={0} style={{ width: 160 }} />
          </Form.Item>
          <Form.Item name="remark" label={t('config.remark')}>
            <Input.TextArea autoSize={{ minRows: 2 }} />
          </Form.Item>
        </Form>
      </FormContainer>
    </>
  )
}
