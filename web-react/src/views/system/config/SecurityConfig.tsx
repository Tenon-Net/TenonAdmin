// 安全策略 = 结构化表单(GroupCode='security'):登录锁定 / 密码复杂度 / 会话时长 / 验证码 / 短信 / 限流。
// 后端 ISecurityPolicyProvider 读这些键强制执行,改值即时生效、无需重发。载入/序列化纯逻辑在 configForm.ts(变异钉)。
import { useEffect, useState } from 'react'
import { App, Button, Col, Divider, Form, InputNumber, Row, Select, Spin, Switch } from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { Can } from '@/components/Can'
import { configApi } from '@/api'
import { translateError } from '@/utils/error'
import {
  CAPTCHA_KEY, PWD_BOOL_FIELDS, RATELIMIT_KEY, SMS_LOGIN_KEY, SMS_MFA_KEY,
  parseSecurity, serializeSecurity, type SecurityState,
} from './configForm'
import HighSensConfig from './HighSensConfig'

const CAPTCHA_TYPES = ['char', 'path', 'math']

export default function SecurityConfig() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const [s, setS] = useState<SecurityState>({ nums: {}, bools: {}, captchaType: 'char' })
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    configApi
      .listByGroup('security')
      .then((rows) => setS(parseSecurity(rows)))
      .catch((e) => message.error(translateError(e)))
      .finally(() => setLoading(false))
  }, [message])

  const save = async () => {
    setSaving(true)
    try {
      await configApi.saveBatch(serializeSecurity(s))
      message.success(t('config.saved'))
    } catch (e) {
      message.error(translateError(e))
    } finally {
      setSaving(false)
    }
  }

  // 'sys.security.loginLock.maxFailCount' → t('config.security.loginLock.maxFailCount')
  const label = (key: string) => t('config.security.' + key.replace('sys.security.', ''))
  const setNum = (key: string, min: number) => (v: number | null) =>
    setS((p) => ({ ...p, nums: { ...p.nums, [key]: v ?? min } }))
  const setBool = (key: string) => (v: boolean) => setS((p) => ({ ...p, bools: { ...p.bools, [key]: v } }))

  const numItem = (key: string, min: number) => (
    <Col key={key} xs={24} sm={12}>
      <Form.Item label={label(key)}>
        <InputNumber min={min} value={s.nums[key]} onChange={setNum(key, min)} style={{ width: 160 }} />
      </Form.Item>
    </Col>
  )
  const boolItem = (key: string) => (
    <Col key={key} xs={24} sm={12}>
      <Form.Item label={label(key)}>
        <Switch checked={!!s.bools[key]} onChange={setBool(key)} />
      </Form.Item>
    </Col>
  )

  return (
    <Spin spinning={loading}>
      <Form layout="vertical" style={{ maxWidth: 960 }}>
        <Divider titlePlacement="start">{t('config.security.loginLock.title')}</Divider>
        <Row gutter={32}>
          {numItem('sys.security.loginLock.maxFailCount', 0)}
          {numItem('sys.security.loginLock.lockMinutes', 1)}
        </Row>

        <Divider titlePlacement="start">{t('config.security.password.title')}</Divider>
        <Row gutter={32}>
          {numItem('sys.security.password.minLength', 1)}
          {numItem('sys.security.password.expireDays', 0)}
          {PWD_BOOL_FIELDS.map((k) => boolItem(k))}
        </Row>

        <Divider titlePlacement="start">{t('config.security.session.title')}</Divider>
        <Row gutter={32}>
          {numItem('sys.security.session.accessMinutes', 1)}
          {numItem('sys.security.session.refreshMinutes', 1)}
        </Row>

        <Divider titlePlacement="start">{t('config.security.captcha.title')}</Divider>
        <Row gutter={32}>
          {boolItem(CAPTCHA_KEY)}
          <Col xs={24} sm={12}>
            <Form.Item label={t('config.security.captcha.type')}>
              <Select
                value={s.captchaType}
                onChange={(v) => setS((p) => ({ ...p, captchaType: v }))}
                options={CAPTCHA_TYPES.map((v) => ({ label: t('config.security.captcha.types.' + v), value: v }))}
                style={{ width: 220 }}
              />
            </Form.Item>
          </Col>
        </Row>

        <Divider titlePlacement="start">{t('config.security.sms.title')}</Divider>
        <Row gutter={32}>
          {boolItem(SMS_MFA_KEY)}
          {boolItem(SMS_LOGIN_KEY)}
        </Row>
        {/* 短信通道提示:内核默认 LoggingSmsSender 只写日志,生产须注册真实 ISmsSender */}
        <p style={{ margin: '-6px 0 12px', fontSize: 12, color: 'var(--color-text-tertiary)' }}>
          {t('config.security.sms.hint')}
        </p>

        <Divider titlePlacement="start">{t('config.security.rateLimit.title')}</Divider>
        <Row gutter={32}>
          {boolItem(RATELIMIT_KEY)}
          {numItem('sys.security.rateLimit.windowSeconds', 1)}
          {numItem('sys.security.rateLimit.permitPerWindow', 0)}
          {numItem('sys.security.rateLimit.authPermitPerWindow', 0)}
        </Row>

        <Divider titlePlacement="start">{t('config.security.highSens.title')}</Divider>
        <HighSensConfig />

        <Can code="PUT:/api/v1/sys/config/batch">
          <Button
            type="primary"
            loading={saving}
            onClick={save}
            icon={<AppIcon icon="ph:floppy-disk" size={16} />}
            style={{ marginTop: 8 }}
          >
            {t('common.save')}
          </Button>
        </Can>
      </Form>
    </Spin>
  )
}
