// 接入应用新增/编辑。编码创建后不可改(调用记录与排障的稳定锚点);启停走列表行内开关的独立接口。
import { forwardRef, useImperativeHandle, useState } from 'react'
import { App, Form, Input, InputNumber, Switch } from 'antd'
import { useTranslation } from 'react-i18next'
import { FormContainer } from '@/components/FormContainer'
import { OrgTreeSelect } from '@/components/OrgTreeSelect'
import { integrationAppApi } from '@/api/integration'
import { useAuthStore } from '@/stores/auth'
import { translateError } from '@/utils/error'
import type { IntegrationAppRow } from '@/types/integration'

export interface AppFormModalHandle {
  openAdd: () => void
  openEdit: (row: IntegrationAppRow) => void
}

interface AppForm {
  code: string
  name: string
  description?: string
  ownerOrgId?: number | null
  rateLimitPerMinute?: number | null
  enabled: boolean
}

const CODE_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{1,63}$/

export const AppFormModal = forwardRef<AppFormModalHandle, { onSaved: () => void }>(function AppFormModal({ onSaved }, ref) {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const isSuperAdmin = useAuthStore((s) => s.isSuperAdmin)
  const [form] = Form.useForm<AppForm>()
  const [open, setOpen] = useState(false)
  const [editingId, setEditingId] = useState<number | string | null>(null)

  useImperativeHandle(ref, () => ({
    openAdd: () => {
      setEditingId(null)
      form.resetFields()
      form.setFieldsValue({ code: '', name: '', description: '', ownerOrgId: null, rateLimitPerMinute: null, enabled: isSuperAdmin })
      setOpen(true)
    },
    openEdit: (r) => {
      setEditingId(r.id ?? null)
      form.resetFields()
      form.setFieldsValue({
        code: r.code ?? '',
        name: r.name ?? '',
        description: r.description ?? '',
        ownerOrgId: r.ownerOrgId == null ? null : Number(r.ownerOrgId),
        rateLimitPerMinute: r.rateLimitPerMinute == null ? null : Number(r.rateLimitPerMinute),
        enabled: !!r.enabled,
      })
      setOpen(true)
    },
  }), [form, isSuperAdmin])

  const save = async () => {
    const v = await form.validateFields()
    const common = {
      name: v.name.trim(),
      description: v.description?.trim() || null,
      ownerOrgId: v.ownerOrgId ?? null,
      rateLimitPerMinute: v.rateLimitPerMinute ?? null,
    }
    try {
      if (editingId === null) await integrationAppApi.add({ ...common, code: v.code.trim(), enabled: isSuperAdmin && !!v.enabled })
      else await integrationAppApi.update(editingId, common)
      message.success(t('integration.app.saved'))
      onSaved()
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  return (
    <FormContainer
      open={open}
      onOpenChange={setOpen}
      title={editingId === null ? t('integration.app.add') : t('integration.app.edit')}
      confirmText={t('common.save')}
      onConfirm={save}
    >
      <Form form={form} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }} style={{ marginTop: 12 }}>
        <Form.Item
          name="code"
          label={t('integration.app.code')}
          rules={[
            { required: true, whitespace: true, message: t('integration.app.codeRequired') },
            { pattern: CODE_PATTERN, message: t('integration.app.codeHint') },
          ]}
        >
          <Input disabled={editingId !== null} placeholder={t('integration.app.codeHint')} />
        </Form.Item>
        <Form.Item name="name" label={t('integration.app.name')} rules={[{ required: true, whitespace: true, message: t('integration.app.nameRequired') }]}>
          <Input maxLength={64} />
        </Form.Item>
        <Form.Item name="description" label={t('integration.app.description')}>
          <Input.TextArea maxLength={256} autoSize={{ minRows: 2, maxRows: 4 }} />
        </Form.Item>
        <Form.Item name="ownerOrgId" label={t('integration.app.ownerOrg')}>
          <OrgTreeSelect placeholder={t('integration.app.ownerOrgHint')} allowClear disabled={!isSuperAdmin} />
        </Form.Item>
        <Form.Item name="rateLimitPerMinute" label={t('integration.app.rateLimit')} extra={t('integration.app.rateLimitHint')}>
          <InputNumber min={0} max={1000000} style={{ width: '100%' }} />
        </Form.Item>
        {editingId === null && isSuperAdmin && (
          <Form.Item name="enabled" label={t('integration.app.enabled')} valuePropName="checked">
            <Switch />
          </Form.Item>
        )}
      </Form>
    </FormContainer>
  )
})
