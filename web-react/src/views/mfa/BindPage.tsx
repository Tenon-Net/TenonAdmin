import { useState } from 'react'
import { Alert, App, Button, Card, Form, Input, QRCode, Typography } from 'antd'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { mfaApi } from '@/api'
import { translateError } from '@/utils/error'
import { triggerBlobDownload } from '@/utils/download'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'
import { takeRecoveryCodes } from './bindComplete'

type Setup = { bindChallengeId: string; account: string; otpauthUri?: string; seed?: string }

export default function BindPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const storeAccount = useUserStore((s) => s.userInfo?.account ?? '')
  const prefillAccount = params.get('account') ?? storeAccount

  const [mode, setMode] = useState<'bind' | 'recovery'>(
    params.get('mode') === 'recovery' ? 'recovery' : 'bind',
  )
  const [setup, setSetup] = useState<Setup | null>(null)
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null)
  const [recoveryComplete, setRecoveryComplete] = useState(false)
  const [starting, setStarting] = useState(false)
  const [completing, setCompleting] = useState(false)

  const copy = async (value: string) => {
    try {
      await navigator.clipboard.writeText(value)
      message.success(t('mfaBind.copy'))
    } catch {
      message.error(t('user.copyFailed'))
    }
  }

  function backToLogin() {
    // 绑定页可能仍挂着旧登录会话;清会话后再进登录,避免受保护区跳过门户重建。
    useAuthStore.getState().reset()
    useUserStore.getState().clear()
    void navigate('/login', { replace: true })
  }

  /** 恢复码下载到本地 txt(一次性展示后用户可离线保管)。 */
  const downloadRecoveryCodes = (codes: string[]) => {
    const account = (setup?.account || params.get('account') || storeAccount || 'account').trim() || 'account'
    const lines = [
      t('mfaBind.recoveryDownloadHeader', { account }),
      '',
      ...codes,
      '',
      t('mfaBind.recoveryDownloadFooter'),
    ]
    const stamp = new Date().toISOString().slice(0, 10)
    const safe = account.replace(/[^\w.\-@]+/g, '_')
    triggerBlobDownload(
      new Blob([lines.join('\n')], { type: 'text/plain;charset=utf-8' }),
      `tenon-recovery-codes-${safe}-${stamp}.txt`,
    )
    message.success(t('mfaBind.recoveryDownloaded'))
  }

  const start = async ({ account, password }: { account: string; password: string }) => {
    setStarting(true)
    try {
      const out = await mfaApi.bindStart({
        account: account.trim(),
        currentPassword: password,
      })
      if (!out.bindChallengeId || !out.otpauthUri || !out.seed) {
        message.error(t('mfaBind.startResponseIncomplete'))
        return
      }
      setSetup({
        bindChallengeId: out.bindChallengeId,
        account: account.trim(),
        otpauthUri: out.otpauthUri ?? undefined,
        seed: out.seed ?? undefined,
      })
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setStarting(false)
    }
  }

  const complete = async ({ code }: { code: string }) => {
    if (!setup) return
    setCompleting(true)
    try {
      const out = await mfaApi.bindComplete({ bindChallengeId: setup.bindChallengeId, totpCode: code })
      const codes = takeRecoveryCodes(out)
      if (!codes) {
        message.error(t('mfaBind.completeResponseIncomplete'))
        return
      }
      setRecoveryCodes(codes)
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setCompleting(false)
    }
  }

  const recover = async ({ account, password, recoveryCode }: { account: string; password: string; recoveryCode: string }) => {
    setCompleting(true)
    try {
      await mfaApi.recovery({ account, currentPassword: password, recoveryCode })
      setRecoveryComplete(true)
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setCompleting(false)
    }
  }

  return (
    <main style={{ minHeight: '100vh', display: 'grid', placeItems: 'center', padding: 24 }}>
      <Card
        title={mode === 'bind' ? t('mfaBind.title') : t('mfaBind.recoveryModeTitle')}
        extra={(
          <Button
            type="link"
            onClick={() => {
              setMode(mode === 'bind' ? 'recovery' : 'bind')
              setRecoveryComplete(false)
              if (mode === 'bind') {
                setSetup(null)
                setRecoveryCodes(null)
              }
            }}
          >
            {mode === 'bind' ? t('mfaBind.useRecovery') : t('mfaBind.backToBind')}
          </Button>
        )}
        style={{ width: 'min(100%, 560px)' }}
      >
        {mode === 'recovery' ? (
          recoveryComplete ? (
            <>
              <Alert type="success" showIcon title={t('mfaBind.recoveryComplete')} description={t('mfaBind.recoveryRebind')} />
              <Button type="primary" block style={{ marginTop: 16 }} onClick={() => backToLogin()}>
                {t('mfaBind.backToLogin')}
              </Button>
            </>
          ) : (
            <Form layout="vertical" onFinish={recover} initialValues={{ account: prefillAccount }}>
              <Alert type="warning" showIcon title={t('mfaBind.recoveryModeHint')} style={{ marginBottom: 16 }} />
              <Form.Item name="account" label={t('mfaBind.account')} rules={[{ required: true, message: t('mfaBind.accountRequired') }]}>
                <Input autoComplete="username" />
              </Form.Item>
              <Form.Item name="password" label={t('mfaBind.password')} rules={[{ required: true, message: t('mfaBind.passwordRequired') }]}>
                <Input.Password autoComplete="current-password" />
              </Form.Item>
              <Form.Item name="recoveryCode" label={t('mfaBind.recoveryCode')} rules={[{ required: true, message: t('mfaBind.recoveryCodeRequired') }]}>
                <Input autoComplete="one-time-code" />
              </Form.Item>
              <Button type="primary" htmlType="submit" loading={completing} block>{t('mfaBind.submitRecovery')}</Button>
            </Form>
          )
        ) : recoveryCodes ? (
          <>
            <Typography.Title level={4}>{t('mfaBind.recoveryTitle')}</Typography.Title>
            <Alert type="warning" showIcon title={t('mfaBind.recoveryHint')} style={{ marginBottom: 12 }} />
            <Input.TextArea readOnly value={recoveryCodes.join('\n')} autoSize={{ minRows: 8 }} />
            <Button type="primary" block style={{ marginTop: 12 }} onClick={() => downloadRecoveryCodes(recoveryCodes)}>
              {t('mfaBind.downloadCodes')}
            </Button>
            <Button block style={{ marginTop: 8 }} onClick={() => void copy(recoveryCodes.join('\n'))}>
              {t('mfaBind.copy')}
            </Button>
            <Button block style={{ marginTop: 8 }} onClick={() => backToLogin()}>
              {t('mfaBind.backToLogin')}
            </Button>
          </>
        ) : !setup ? (
          <Form layout="vertical" onFinish={start} initialValues={{ account: prefillAccount }}>
            <Alert type="info" showIcon title={t('mfaBind.bindHint')} style={{ marginBottom: 16 }} />
            <Form.Item name="account" label={t('mfaBind.account')} rules={[{ required: true, message: t('mfaBind.accountRequired') }]}>
              <Input autoComplete="username" />
            </Form.Item>
            <Form.Item name="password" label={t('mfaBind.password')} rules={[{ required: true, message: t('mfaBind.passwordRequired') }]}>
              <Input.Password autoComplete="current-password" />
            </Form.Item>
            <Button type="primary" htmlType="submit" loading={starting} block>{t('mfaBind.begin')}</Button>
          </Form>
        ) : (
          <>
            <Alert type="info" showIcon title={t('mfaBind.scanSetupHint')} style={{ marginBottom: 16 }} />
            {setup.otpauthUri ? (
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'center',
                  marginBottom: 16,
                  padding: 16,
                  background: '#fff',
                  borderRadius: 8,
                  border: '1px solid var(--ant-color-border, #e5e7eb)',
                }}
              >
                <QRCode value={setup.otpauthUri} size={200} errorLevel="M" />
              </div>
            ) : null}
            {setup.seed ? (
              <Form.Item label={t('mfaBind.seed')} style={{ marginBottom: 8 }}>
                <Input
                  readOnly
                  value={setup.seed}
                  suffix={<Button type="link" size="small" style={{ padding: 0 }} onClick={() => void copy(setup.seed!)}>{t('mfaBind.copy')}</Button>}
                />
              </Form.Item>
            ) : null}
            <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginBottom: 16 }}>
              {t('mfaBind.manualSetupHint')}
            </Typography.Paragraph>
            <Form layout="vertical" onFinish={complete}>
              <Form.Item name="code" label={t('mfaBind.code')} rules={[{ required: true, pattern: /^\d{6}$/, message: t('mfaBind.codeRequired') }]}>
                <Input inputMode="numeric" autoComplete="one-time-code" maxLength={6} />
              </Form.Item>
              <Button type="primary" htmlType="submit" loading={completing} block>{t('mfaBind.complete')}</Button>
            </Form>
          </>
        )}
      </Card>
    </main>
  )
}
