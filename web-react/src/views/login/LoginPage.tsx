import { useEffect, useState } from 'react'
import { App, Button, Card, Checkbox, Form, Input, Typography } from 'antd'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { authApi } from '@/api'
import { useUserStore } from '@/stores/user'
import { useSiteStore, appVersion } from '@/stores/site'
import { translateError } from '@/utils/error'

interface FormValues {
  account: string
  password: string
  captchaCode?: string
  remember: boolean
}

/**
 * 登录页(第一套皮肤)。对应 Vue 侧 `views/login/` 的账号密码那条路径。
 *
 * **暂不含**短信免密登录 / 短信二次验证 / 外部登录(SSO)—— 那三块在 Vue 侧占了 `LoginForm.vue`
 * 一半篇幅,各自要后端端点与倒计时状态机,留给后续批次。这里先把「账号密码 + 图形验证码 + 品牌信息」
 * 这条主干打通,免得一次改动同时压上四条链路。
 */
export default function LoginPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const site = useSiteStore((s) => s.site)
  const loadSite = useSiteStore((s) => s.load)
  const setSession = useUserStore((s) => s.setSession)

  const [loading, setLoading] = useState(false)
  const [captcha, setCaptcha] = useState<{ id: string; svg: string; type: string } | null>(null)
  const [form] = Form.useForm<FormValues>()

  async function loadCaptcha() {
    try {
      const c = await authApi.captcha()
      setCaptcha({ id: c.captchaId, svg: c.svg, type: c.type || 'char' })
    } catch {
      // 拉取失败不阻塞登录页渲染;点击图形可重试
    }
  }

  useEffect(() => {
    // 站点信息全站共用(store 内部去重);验证码开关据此**运行时驱动**,启用才拉图。
    void loadSite().then(() => {
      if (useSiteStore.getState().site.captchaEnabled) void loadCaptcha()
    })
  }, [loadSite])

  /** 验证码一次性消费:任何用掉票据的请求失败后必刷新,避免复用作废票据。 */
  async function refreshCaptchaAfterUse() {
    if (site.captchaEnabled) {
      form.setFieldValue('captchaCode', '')
      await loadCaptcha()
    }
  }

  async function onFinish(values: FormValues) {
    setLoading(true)
    try {
      const res = await authApi.login({
        account: values.account,
        password: values.password,
        captchaId: captcha?.id,
        captchaCode: values.captchaCode,
      })
      setSession(res)
      message.success(t('login.success'))
      void navigate('/', { replace: true })
    } catch (err) {
      message.error(translateError(err))
      await refreshCaptchaAfterUse()
    } finally {
      setLoading(false)
    }
  }

  return (
    <div style={{ minHeight: '100vh', display: 'grid', placeItems: 'center', padding: 24 }}>
      <Card style={{ width: 380 }} data-testid="login-card">
        <div style={{ textAlign: 'center', marginBottom: 24 }}>
          {/* logo 由后端 sys_config 下发(可空);空时不占位,标题自己往上顶。 */}
          {site.logo ? (
            <img src={site.logo} alt="" style={{ height: 44, marginBottom: 12 }} />
          ) : null}
          <Typography.Title level={4} style={{ margin: 0 }}>
            {site.title}
          </Typography.Title>
          {site.subtitle ? (
            <Typography.Text type="secondary">{site.subtitle}</Typography.Text>
          ) : null}
        </div>

        <Form<FormValues>
          form={form}
          layout="vertical"
          requiredMark={false}
          // ponytail: 开发环境预填超管账号密码,免得每次手敲;生产构建下 import.meta.env.DEV 为 false,留空。
          initialValues={
            import.meta.env.DEV
              ? { account: 'superAdmin', password: 'Aa123456', remember: true }
              : { account: '', password: '', remember: true }
          }
          onFinish={(v) => void onFinish(v)}
        >
          <Form.Item name="account" label={t('login.account')} rules={[{ required: true, message: t('login.required') }]}>
            <Input size="large" autoComplete="username" placeholder={t('login.accountPlaceholder')} />
          </Form.Item>

          <Form.Item name="password" label={t('login.password')} rules={[{ required: true, message: t('login.required') }]}>
            <Input.Password size="large" autoComplete="current-password" placeholder={t('login.passwordPlaceholder')} />
          </Form.Item>

          {site.captchaEnabled ? (
            <Form.Item label={t('login.captcha')} required>
              <div style={{ display: 'flex', gap: 8 }}>
                <Form.Item name="captchaCode" noStyle rules={[{ required: true, message: t('login.captchaRequired') }]}>
                  <Input
                    size="large"
                    placeholder={captcha?.type === 'math' ? t('login.captchaMathPlaceholder') : t('login.captchaPlaceholder')}
                  />
                </Form.Item>
                {/* 后端下发的是 SVG 源码。这是本模板唯一一处 dangerouslySetInnerHTML,来源是自家后端的
                    验证码端点(与 Vue 侧 `v-html` 同一处理);**别把这个先例扩大到任何用户输入上**。
                    内置三个 provider 的 SVG 全服务端自生成、零请求输入入串,可信;但 ICaptchaProvider
                    是可替换扩展点,契约见后端 `ICaptchaProvider` 注释(Svg 不得嵌入不可信输入)。 */}
                {/* 键盘可达:验证码看不清要能换一张,不能只有鼠标能点。role+tabIndex+Enter/Space+aria-label。 */}
                <div
                  role="button"
                  tabIndex={0}
                  aria-label={t('login.captcha')}
                  onClick={() => void loadCaptcha()}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault()
                      void loadCaptcha()
                    }
                  }}
                  title={t('login.captcha')}
                  style={{ cursor: 'pointer', display: 'grid', placeItems: 'center', minWidth: 110 }}
                  data-testid="captcha-img"
                  dangerouslySetInnerHTML={{ __html: captcha?.svg ?? '' }}
                />
              </div>
            </Form.Item>
          ) : null}

          <Form.Item name="remember" valuePropName="checked">
            <Checkbox>{t('login.remember')}</Checkbox>
          </Form.Item>

          <Button type="primary" size="large" block htmlType="submit" loading={loading}>
            {t('login.submit')}
          </Button>
        </Form>

        <div style={{ textAlign: 'center', marginTop: 16 }}>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {site.copyright ? (
              site.copyrightUrl ? (
                <a href={site.copyrightUrl} target="_blank" rel="noreferrer">
                  {site.copyright}
                </a>
              ) : (
                site.copyright
              )
            ) : null}
            {appVersion ? ` v${appVersion}` : ''}
          </Typography.Text>
        </div>
      </Card>
    </div>
  )
}
