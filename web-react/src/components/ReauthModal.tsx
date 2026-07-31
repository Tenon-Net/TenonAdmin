// Level3 高危写操作再认证弹窗:client 中间件遇 40024 时经 reauthGate 唤起。
import { useEffect, useRef, useState } from 'react'
import { Input, Modal, Radio, Space, message } from 'antd'
import { authApi } from '@/api'
import { registerReauthHandler } from '@/api/reauthGate'
import { t } from '@/locales'
import { translateError } from '@/utils/error'

export function ReauthModal() {
  const [open, setOpen] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [method, setMethod] = useState<'totp' | 'password'>('totp')
  const [totpCode, setTotpCode] = useState('')
  const [password, setPassword] = useState('')
  const settleRef = useRef<((ok: boolean) => void) | null>(null)

  useEffect(() => {
    registerReauthHandler(
      () =>
        new Promise<boolean>((resolve) => {
          settleRef.current = resolve
          setMethod('totp')
          setTotpCode('')
          setPassword('')
          setOpen(true)
        }),
    )
    return () => {
      registerReauthHandler(null)
      settleRef.current?.(false)
      settleRef.current = null
    }
  }, [])

  function finish(ok: boolean) {
    setOpen(false)
    setSubmitting(false)
    const r = settleRef.current
    settleRef.current = null
    r?.(ok)
  }

  async function confirm() {
    if (method === 'totp' && !totpCode.trim()) {
      message.warning(t('reauth.totpRequired'))
      return
    }
    if (method === 'password' && !password) {
      message.warning(t('reauth.passwordRequired'))
      return
    }
    setSubmitting(true)
    try {
      await authApi.reauth({
        method,
        totpCode: method === 'totp' ? totpCode.trim() : undefined,
        password: method === 'password' ? password : undefined,
      })
      finish(true)
    } catch (e) {
      message.error(translateError(e))
      setSubmitting(false)
    }
  }

  return (
    <Modal
      open={open}
      title={t('reauth.title')}
      okText={t('reauth.confirm')}
      cancelText={t('common.cancel')}
      confirmLoading={submitting}
      maskClosable={false}
      keyboard={!submitting}
      onOk={() => void confirm()}
      onCancel={() => finish(false)}
      destroyOnHidden
    >
      <p style={{ margin: '0 0 12px', color: 'var(--color-text-tertiary)', fontSize: 13 }}>
        {t('reauth.hint')}
      </p>
      <Space direction="vertical" style={{ width: '100%' }} size="middle">
        <div>
          <div style={{ marginBottom: 6 }}>{t('reauth.method')}</div>
          <Radio.Group
            value={method}
            onChange={(e) => setMethod(e.target.value as 'totp' | 'password')}
            options={[
              { value: 'totp', label: t('reauth.methodTotp') },
              { value: 'password', label: t('reauth.methodPassword') },
            ]}
          />
        </div>
        {method === 'totp' ? (
          <Input
            value={totpCode}
            onChange={(e) => setTotpCode(e.target.value)}
            placeholder={t('reauth.totpPlaceholder')}
            maxLength={8}
            onPressEnter={() => void confirm()}
          />
        ) : (
          <Input.Password
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder={t('reauth.passwordPlaceholder')}
            onPressEnter={() => void confirm()}
          />
        )}
      </Space>
    </Modal>
  )
}
