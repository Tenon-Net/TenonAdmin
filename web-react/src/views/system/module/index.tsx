// 应用(模块)管理 = 标准 CRUD:moduleApi.list 静态列表 + 表单弹窗。≠ B7 门户选择器(那是切应用)。
// 内置 system 应用受保护:禁删 + 禁停(停了门户失联,模块/菜单管理页都在它下面)。无独立启停端点 → StatusSwitch 走全量 update。
// 纯逻辑抽 moduleForm.ts(变异钉),本页接线。
import { useCallback, useEffect, useMemo, useState } from 'react'
import { App, Button, Card, Form, Input, InputNumber, Space, Switch, Table, Tag, type TableColumnsType } from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { Can } from '@/components/Can'
import { FormContainer } from '@/components/FormContainer'
import { IconPicker } from '@/components/IconPicker'
import { StatusSwitch } from '@/components/StatusSwitch'
import { useConfirm } from '@/hooks/useConfirm'
import { useHasPerm } from '@/stores/auth'
import { moduleApi } from '@/api'
import { translateError } from '@/utils/error'
import type { ModuleInput, ModuleRow } from '@/types/api'
import { blankModule, isBuiltin, moduleToInput } from './moduleForm'

export default function ModulePage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { confirm } = useConfirm()
  const has = useHasPerm()

  const [rows, setRows] = useState<ModuleRow[]>([])
  const [loading, setLoading] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setRows(await moduleApi.list())
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setLoading(false)
    }
  }, [message])
  useEffect(() => { void load() }, [load])

  // ── 新增/编辑弹窗 ──
  const [form] = Form.useForm<ModuleInput>()
  const [open, setOpen] = useState(false)
  const [editingId, setEditingId] = useState<number | null>(null)

  const openAdd = useCallback(() => { setEditingId(null); form.setFieldsValue(blankModule()); setOpen(true) }, [form])
  const openEdit = useCallback((r: ModuleRow) => { setEditingId(r.id); form.setFieldsValue(moduleToInput(r)); setOpen(true) }, [form])
  const save = async () => {
    const v = await form.validateFields() // 表单渲染全部 8 字段 → validateFields 即全量(无 C9 那种未注册漏字段)
    try {
      if (editingId === null) await moduleApi.add(v)
      else await moduleApi.update(editingId, v)
      message.success(t('module.saved'))
      await load()
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  const handleDelete = useCallback((r: ModuleRow) => {
    confirm({ content: t('module.deleteConfirm', { title: r.title }), action: () => moduleApi.remove(r.id), successMsg: t('module.deleted') }).then(
      (ok) => { if (ok) void load() },
    )
  }, [confirm, t, load])

  const columns = useMemo<TableColumnsType<ModuleRow>>(() => [
    { title: t('module.code'), dataIndex: 'code', ellipsis: true },
    {
      title: t('module.name'), dataIndex: 'title',
      render: (_, r) => (
        <Space size={6}>
          {r.icon ? <AppIcon icon={r.icon} size={18} /> : null}
          {r.title}
          {isBuiltin(r) && <Tag variant="filled" color="blue">{t('module.builtin')}</Tag>}
        </Space>
      ),
    },
    { title: t('module.defaultRoute'), dataIndex: 'defaultRoute', ellipsis: true, render: (_, r) => r.defaultRoute || '—' },
    { title: t('module.apiPrefix'), dataIndex: 'apiPrefix', width: 120, render: (_, r) => r.apiPrefix || '—' },
    { title: t('module.sort'), dataIndex: 'sort', width: 80 },
    {
      title: t('common.status'), dataIndex: 'enabled', width: 90,
      // 无启停权限者退化为只读状态标签(保留可见性);内置应用禁停(停了门户失联)。
      render: (_, r) =>
        has('PUT:/api/v1/sys/module/{id}') ? (
          <StatusSwitch
            value={r.enabled}
            disabled={isBuiltin(r)}
            confirm={(next) => (next ? null : t('module.disableConfirm', { title: r.title }))}
            request={(next) => moduleApi.update(r.id, { ...moduleToInput(r), enabled: next })}
            onChange={() => void load()}
          />
        ) : (
          <Tag variant="filled" color={r.enabled ? 'green' : undefined}>{t(r.enabled ? 'common.enabled' : 'common.disabled')}</Tag>
        ),
    },
    {
      title: t('common.operation'), key: 'op', width: 140,
      render: (_, r) => (
        <Space size={4}>
          {has('PUT:/api/v1/sys/module/{id}') && <Button type="link" size="small" onClick={() => openEdit(r)}>{t('common.edit')}</Button>}
          {/* 内置应用禁删(后端 42013 兜底) */}
          {!isBuiltin(r) && has('DELETE:/api/v1/sys/module/{id}') && (
            <Button type="link" size="small" danger onClick={() => handleDelete(r)}>{t('common.delete')}</Button>
          )}
        </Space>
      ),
    },
  ], [t, has, openEdit, handleDelete, load])

  return (
    <Card>
      <div style={{ marginBottom: 12 }}>
        <Can code="POST:/api/v1/sys/module/add">
          <Button type="primary" onClick={openAdd}>{t('common.add')}</Button>
        </Can>
      </div>
      <Table<ModuleRow> rowKey="id" columns={columns} dataSource={rows} loading={loading} pagination={false} size="small" />

      <FormContainer
        open={open}
        onOpenChange={setOpen}
        title={editingId === null ? t('module.addTitle') : t('module.editTitle')}
        width={520}
        confirmText={t('common.save')}
        onConfirm={save}
      >
        <Form form={form} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }} style={{ marginTop: 12 }}>
          <Form.Item name="code" label={t('module.code')} rules={[{ required: true, whitespace: true, message: t('module.codeRequired') }]}>
            {/* 应用编码建后不可改;编辑时置灰 */}
            <Input disabled={editingId !== null} placeholder={t('module.code')} />
          </Form.Item>
          <Form.Item name="title" label={t('module.name')} rules={[{ required: true, whitespace: true, message: t('module.nameRequired') }]}>
            <Input placeholder={t('module.name')} />
          </Form.Item>
          <Form.Item name="icon" label={t('module.icon')}>
            <IconPicker />
          </Form.Item>
          <Form.Item name="defaultRoute" label={t('module.defaultRoute')}>
            <Input placeholder="/system/user" />
          </Form.Item>
          <Form.Item name="apiPrefix" label={t('module.apiPrefix')} extra={t('module.apiPrefixHint')}>
            <Input placeholder="sys" />
          </Form.Item>
          <Form.Item name="sort" label={t('module.sort')}>
            <InputNumber min={0} style={{ width: 160 }} />
          </Form.Item>
          <Form.Item name="enabled" label={t('common.status')} valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="remark" label={t('module.remark')}>
            <Input.TextArea autoSize={{ minRows: 2 }} />
          </Form.Item>
        </Form>
      </FormContainer>
    </Card>
  )
}
