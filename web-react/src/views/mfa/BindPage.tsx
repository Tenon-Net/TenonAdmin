import { useState } from 'react'
import { Alert, App, Button, Card, Form, Input, Typography } from 'antd'
import { useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { mfaApi } from '@/api'
import { translateError } from '@/utils/error'
import { takeRecoveryCodes } from './bindComplete'

type Setup = { bindChallengeId: string; otpauthUri?: string; seed?: string }

export default function BindPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const [mode, setMode] = useState<'bind' | 'recovery'>('bind')
  const [setup, setSetup] = useState<Setup | null>(null)
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null)
  const [recoveryComplete, setRecoveryComplete] = useState(false)
  const [starting, setStarting] = useState(false)
  const [completing, setCompleting] = useState(false)

  const copy = async (value: string) => {
    try {
      await navigator.clipboard.writeText(value)
      message.success(t('common.copied'))
    } catch {
      message.error(t('user.copyFailed'))
    }
  }
  const start = async ({ password }: { password: string }) => {
    setStarting(true)
    try {
      const out = await mfaApi.bindStart({ token, currentPassword: password })
      if (!out.bindChallengeId || !out.otpauthUri || !out.seed) {
        message.error(t('mfaBind.startResponseIncomplete'))
        return
      }
      setSetup({ bindChallengeId: out.bindChallengeId, otpauthUri: out.otpauthUri ?? undefined, seed: out.seed ?? undefined })
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
      // 账号此时服务端已绑定：绝不能因缺恢复码仍切入“成功展示”空屏。
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
        extra={<Button type="link" onClick={() => { setMode(mode === 'bind' ? 'recovery' : 'bind'); setRecoveryComplete(false) }}>{mode === 'bind' ? t('mfaBind.useRecovery') : t('mfaBind.backToBind')}</Button>}
        style={{ width: 'min(100%, 560px)' }}
      >
        {mode === 'recovery' ? recoveryComplete ? (
          <Alert type="success" showIcon message={t('mfaBind.recoveryComplete')} description={t('mfaBind.recoveryRebind')} />
        ) : (
          <Form layout="vertical" onFinish={recover}>
            <Alert type="warning" showIcon message={t('mfaBind.recoveryModeHint')} style={{ marginBottom: 16 }} />
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
        ) : !token ? <Typography.Text type="danger">{t('mfaBind.tokenRequired')}</Typography.Text> : recoveryCodes ? (
          <>
            <Typography.Title level={4}>{t('mfaBind.recoveryTitle')}</Typography.Title>
            <Typography.Paragraph>{t('mfaBind.recoveryHint')}</Typography.Paragraph>
            <Input.TextArea readOnly value={recoveryCodes.join('\n')} autoSize={{ minRows: 8 }} />
            <Button style={{ marginTop: 12 }} onClick={() => void copy(recoveryCodes.join('\n'))}>{t('mfaBind.copy')}</Button>
          </>
        ) : !setup ? (
          <Form layout="vertical" onFinish={start}>
            <Form.Item name="password" label={t('mfaBind.password')} rules={[{ required: true, message: t('mfaBind.passwordRequired') }]}>
              <Input.Password autoComplete="current-password" />
            </Form.Item>
            <Button type="primary" htmlType="submit" loading={starting} block>{t('mfaBind.begin')}</Button>
          </Form>
        ) : (
          <>
            <Typography.Paragraph>{t('mfaBind.setupHint')}</Typography.Paragraph>
            {setup.otpauthUri ? <Form.Item label={t('mfaBind.uri')}><Input.TextArea readOnly value={setup.otpauthUri} autoSize={{ minRows: 3 }} /></Form.Item> : null}
            {setup.seed ? <Form.Item label={t('mfaBind.seed')}><Input readOnly value={setup.seed} /></Form.Item> : null}
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
