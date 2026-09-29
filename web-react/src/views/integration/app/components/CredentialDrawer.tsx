// 凭据管理抽屉:发放 / 轮换(新旧有限并存)/ 调整到期 / 撤销。发放与轮换返回的完整凭据交给 SecretModal 一次性展示。
import { forwardRef, useCallback, useImperativeHandle, useRef, useState } from 'react'
import {
  App, Button, DatePicker, Drawer, Form, Input, InputNumber, Popconfirm, Radio, Result, Space, Table, Tag, type TableColumnsType,
} from 'antd'
import { useTranslation } from 'react-i18next'
import dayjs, { type Dayjs } from 'dayjs'
import { AppIcon } from '@/components/AppIcon'
import { FormContainer } from '@/components/FormContainer'
import { useConfirm } from '@/hooks/useConfirm'
import { useAuthStore, useHasPerm } from '@/stores/auth'
import { integrationAppApi } from '@/api/integration'
import { translateError } from '@/utils/error'
import { CredentialStatus, type IntegrationAppRow, type OpenAppCredential } from '@/types/integration'
import { PERM, credentialStatusTag, fmtDateTime } from '../../shared'
import { SecretModal, type SecretModalHandle } from './SecretModal'

export interface CredentialDrawerHandle {
  open: (row: IntegrationAppRow) => void
}

type ExpiryMode = 'default' | 'date' | 'never'

const toExpiry = (mode: ExpiryMode, at?: Dayjs | null) => ({
  neverExpires: mode === 'never',
  expiresAt: mode === 'date' && at ? at.toISOString() : null,
})

const pastDisabled = (d: Dayjs) => d.isBefore(dayjs().startOf('day'))

export const CredentialDrawer = forwardRef<CredentialDrawerHandle, { onChanged: () => void }>(function CredentialDrawer({ onChanged }, ref) {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { run } = useConfirm()
  const has = useHasPerm()
  const isSuperAdmin = useAuthStore((s) => s.isSuperAdmin)
  const secretRef = useRef<SecretModalHandle>(null)

  const [open, setOpen] = useState(false)
  const [app, setApp] = useState<IntegrationAppRow | null>(null)
  const [rows, setRows] = useState<OpenAppCredential[]>([])
  const [loading, setLoading] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)
  const loadSeq = useRef(0)

  /** 只认最后一次请求的响应:换了应用或连续刷新时,迟到的旧列表一律丢弃。 */
  const load = useCallback(async (target: IntegrationAppRow | null) => {
    if (!target?.id) return
    const seq = ++loadSeq.current
    setLoading(true)
    setLoadError(null)
    try {
      const list = await integrationAppApi.credentials(target.id)
      if (seq === loadSeq.current) setRows(list)
    } catch (e) {
      if (seq !== loadSeq.current) return
      const text = translateError(e)
      message.error(text)
      setLoadError(text)
    } finally {
      if (seq === loadSeq.current) setLoading(false)
    }
  }, [message])

  useImperativeHandle(ref, () => ({
    open: (row) => {
      setApp(row)
      setRows([])
      setLoadError(null)
      setOpen(true)
      void load(row)
    },
  }), [load])

  const afterChange = async () => {
    await load(app)
    onChanged()
  }

  /** 选了「指定时间」却没选时刻:提示并返回 true,调用方据此拦下提交。 */
  const missingDate = (mode: ExpiryMode, at?: Dayjs | null) => {
    if (mode !== 'date' || at) return false
    message.warning(t('integration.credential.expiryRequired'))
    return true
  }

  // ── 发放 ──
  const [issueForm] = Form.useForm<{ name?: string; mode: ExpiryMode; at?: Dayjs | null }>()
  const [issueOpen, setIssueOpen] = useState(false)
  const issueMode = Form.useWatch('mode', issueForm)
  const openIssue = () => {
    issueForm.setFieldsValue({ name: '', mode: 'default', at: null })
    setIssueOpen(true)
  }
  const doIssue = async () => {
    const v = await issueForm.validateFields()
    if (missingDate(v.mode, v.at)) return false
    try {
      const issued = await integrationAppApi.createCredential(app!.id!, { name: v.name?.trim() || null, ...toExpiry(v.mode, v.at) })
      secretRef.current?.open(issued)
      await afterChange()
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  // ── 轮换 ──
  const [rotateForm] = Form.useForm<{ overlapHours: number | null; name?: string; mode: ExpiryMode; at?: Dayjs | null }>()
  const [rotateOpen, setRotateOpen] = useState(false)
  const [rotateTarget, setRotateTarget] = useState<OpenAppCredential | null>(null)
  const rotateMode = Form.useWatch('mode', rotateForm)
  const openRotate = (r: OpenAppCredential) => {
    setRotateTarget(r)
    rotateForm.setFieldsValue({ overlapHours: null, name: '', mode: 'default', at: null })
    setRotateOpen(true)
  }
  const doRotate = async () => {
    const v = await rotateForm.validateFields()
    if (missingDate(v.mode, v.at)) return false
    try {
      const issued = await integrationAppApi.rotateCredential(app!.id!, rotateTarget!.id!, {
        overlapHours: v.overlapHours ?? null,
        name: v.name?.trim() || null,
        ...toExpiry(v.mode, v.at),
      })
      message.success(t('integration.credential.rotated'))
      secretRef.current?.open(issued)
      await afterChange()
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  // ── 调整到期 ──
  const [expiryForm] = Form.useForm<{ mode: Exclude<ExpiryMode, 'default'>; at?: Dayjs | null }>()
  const [expiryOpen, setExpiryOpen] = useState(false)
  const [expiryTarget, setExpiryTarget] = useState<OpenAppCredential | null>(null)
  const expiryMode = Form.useWatch('mode', expiryForm)
  const openExpiry = (r: OpenAppCredential) => {
    setExpiryTarget(r)
    expiryForm.setFieldsValue({ mode: r.expiresAt ? 'date' : 'never', at: r.expiresAt ? dayjs(r.expiresAt) : null })
    setExpiryOpen(true)
  }
  const doExpiry = async () => {
    const v = await expiryForm.validateFields()
    if (missingDate(v.mode, v.at)) return false
    try {
      await integrationAppApi.setCredentialExpiry(app!.id!, expiryTarget!.id!, toExpiry(v.mode, v.at))
      message.success(t('integration.credential.expirySaved'))
      await afterChange()
    } catch (e) {
      message.error(translateError(e))
      return false
    }
  }

  const expiryModes = (withDefault: boolean) => (
    <Radio.Group>
      {withDefault && <Radio value="default">{t('integration.credential.defaultExpiry')}</Radio>}
      <Radio value="date">{t('integration.credential.specifyExpiry')}</Radio>
      <Radio value="never">{t('integration.credential.neverExpires')}</Radio>
    </Radio.Group>
  )

  const columns: TableColumnsType<OpenAppCredential> = [
    {
      title: t('integration.credential.keyId'), dataIndex: 'keyId',
      render: (_, r) => <code style={{ fontFamily: 'var(--font-mono, ui-monospace, monospace)', fontSize: 13 }}>{`tna_${r.keyId}.****`}</code>,
    },
    { title: t('integration.credential.name'), dataIndex: 'name', ellipsis: true, render: (_, r) => r.name || '—' },
    {
      title: t('integration.credential.status'), dataIndex: 'status', width: 90,
      render: (_, r) => {
        const tag = credentialStatusTag[Number(r.status)] ?? credentialStatusTag[CredentialStatus.Active]
        return <Tag variant="filled" color={tag.type}>{t(tag.key)}</Tag>
      },
    },
    {
      title: t('integration.credential.expiresAt'), dataIndex: 'expiresAt', width: 160,
      render: (_, r) => (r.expiresAt ? fmtDateTime(r.expiresAt) : t('integration.credential.neverExpires')),
    },
    { title: t('integration.credential.lastUsed'), dataIndex: 'lastUsedAt', width: 160, render: (_, r) => fmtDateTime(r.lastUsedAt) },
    {
      title: t('common.operation'), key: 'op', width: 200,
      render: (_, r) => {
        const active = Number(r.status) === CredentialStatus.Active
        const revoked = Number(r.status) === CredentialStatus.Revoked
        return (
          <Space size={4}>
            {active && isSuperAdmin && has(PERM.credRotate) && <Button type="link" size="small" onClick={() => openRotate(r)}>{t('integration.credential.rotate')}</Button>}
            {!revoked && isSuperAdmin && has(PERM.credExpiry) && <Button type="link" size="small" onClick={() => openExpiry(r)}>{t('integration.credential.expiry')}</Button>}
            {!revoked && has(PERM.credRevoke) && (
              <Popconfirm
                title={t('integration.credential.revokeConfirm')}
                onConfirm={() => run(() => integrationAppApi.revokeCredential(app!.id!, r.id!), t('integration.credential.revoked')).then((ok) => {
                  if (ok) void afterChange()
                })}
              >
                <Button type="link" size="small" danger>{t('integration.credential.revoke')}</Button>
              </Popconfirm>
            )}
            {revoked && has(PERM.credDelete) && (
              <Popconfirm
                title={t('integration.credential.deleteConfirm')}
                onConfirm={() => run(() => integrationAppApi.deleteCredential(app!.id!, r.id!), t('integration.credential.deleted')).then((ok) => {
                  if (ok) void afterChange()
                })}
              >
                <Button type="link" size="small" danger>{t('common.delete')}</Button>
              </Popconfirm>
            )}
          </Space>
        )
      },
    },
  ]

  return (
    <>
      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        size={820}
        title={t('integration.credential.title', { app: app?.name ?? '' })}
      >
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 12 }}>
          <span style={{ fontFamily: 'var(--font-mono, ui-monospace, monospace)', color: 'var(--color-text-secondary)' }}>{app?.code}</span>
          {isSuperAdmin && has(PERM.credCreate) && (
            <Button type="primary" size="small" icon={<AppIcon icon="ph:key" size={16} />} data-testid="integration-issue" onClick={openIssue}>
              {t('integration.credential.issue')}
            </Button>
          )}
        </div>
        {loadError ? (
          <Result
            status="error"
            title={t('integration.credential.loadFailed')}
            subTitle={loadError}
            extra={<Button type="primary" onClick={() => void load(app)}>{t('integration.credential.retry')}</Button>}
          />
        ) : (
          <Table<OpenAppCredential>
            rowKey={(r) => String(r.id)}
            columns={columns}
            dataSource={rows}
            loading={loading}
            pagination={false}
            size="small"
            locale={{ emptyText: t('integration.credential.empty') }}
          />
        )}
      </Drawer>

      <FormContainer open={issueOpen} onOpenChange={setIssueOpen} title={t('integration.credential.issueTitle')} onConfirm={doIssue} confirmText={t('integration.credential.issue')}>
        <Form form={issueForm} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }} style={{ marginTop: 12 }}>
          <Form.Item name="name" label={t('integration.credential.name')}>
            <Input maxLength={64} placeholder={t('integration.credential.namePlaceholder')} />
          </Form.Item>
          <Form.Item name="mode" label={t('integration.credential.expiresAt')}>{expiryModes(true)}</Form.Item>
          {issueMode === 'date' && (
            <Form.Item name="at" label=" " colon={false}>
              <DatePicker showTime disabledDate={pastDisabled} style={{ width: '100%' }} />
            </Form.Item>
          )}
        </Form>
      </FormContainer>

      <FormContainer open={rotateOpen} onOpenChange={setRotateOpen} title={t('integration.credential.rotateTitle')} onConfirm={doRotate} confirmText={t('integration.credential.rotate')}>
        <Form form={rotateForm} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }} style={{ marginTop: 12 }}>
          <Form.Item name="overlapHours" label={t('integration.credential.overlapHours')} extra={t('integration.credential.overlapHint')}>
            <InputNumber min={0} placeholder={t('integration.credential.overlapPlaceholder')} style={{ width: '100%' }} data-testid="integration-overlap" />
          </Form.Item>
          <Form.Item name="name" label={t('integration.credential.name')}>
            <Input maxLength={64} placeholder={rotateTarget?.name || t('integration.credential.namePlaceholder')} />
          </Form.Item>
          <Form.Item name="mode" label={t('integration.credential.expiresAt')}>{expiryModes(true)}</Form.Item>
          {rotateMode === 'date' && (
            <Form.Item name="at" label=" " colon={false}>
              <DatePicker showTime disabledDate={pastDisabled} style={{ width: '100%' }} />
            </Form.Item>
          )}
        </Form>
      </FormContainer>

      <FormContainer open={expiryOpen} onOpenChange={setExpiryOpen} title={t('integration.credential.expiryTitle')} width={460} onConfirm={doExpiry} confirmText={t('common.save')}>
        <Form form={expiryForm} style={{ marginTop: 12 }}>
          <Form.Item name="mode">{expiryModes(false)}</Form.Item>
          {expiryMode === 'date' && (
            <Form.Item name="at">
              <DatePicker showTime disabledDate={pastDisabled} style={{ width: '100%' }} />
            </Form.Item>
          )}
        </Form>
      </FormContainer>

      <SecretModal ref={secretRef} />
    </>
  )
})
