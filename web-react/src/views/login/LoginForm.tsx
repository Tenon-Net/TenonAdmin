import { useEffect, useState } from 'react'
import { App, Button, Checkbox, Form, Input } from 'antd'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { authApi } from '@/api'
import { useUserStore } from '@/stores/user'
import { useSiteStore, appVersion } from '@/stores/site'
import { translateError } from '@/utils/error'
import { TenonLogo } from '@/components/TenonLogo'
import './loginform.css'

interface FormValues {
  account: string
  password: string
  captchaCode?: string
  remember: boolean
}

/**
 * 共享登录表单(账号密码路径)。移植 Vue `views/login/LoginForm.vue` 的账号密码那条主干。
 *
 * 三套皮肤(Spotlight/SplitPanel/AuroraGlass)各自提供卡片 chrome + 背景,只内嵌这个表单——
 * 故本组件不再自带 100vh 居中与 Card 外壳,只排版 品牌 / 表单 / 页脚 三段。皮肤外壳自绘品牌栏时
 * (双栏)传 `showBrand={false}` / `showFooter={false}` 关掉卡内重复。文字色走 `--lf-title` / `--lf-hint`
 * 变量(默认跟随应用令牌;深色玻璃皮肤覆写为浅色)。
 *
 * **暂不含** 短信免密 / 短信二次验证 / 外部登录(SSO)—— 与 Vue 侧一致地留给后续批次。
 */
export function LoginForm({ showBrand = true, showFooter = true }: { showBrand?: boolean; showFooter?: boolean }) {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const site = useSiteStore((s) => s.site)
  const loadSite = useSiteStore((s) => s.load)
  const setSession = useUserStore((s) => s.setSession)
  const year = new Date().getFullYear()

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
    <div className="login-form" data-testid="login-card">
      {showBrand ? (
        <>
          <div className="lf-brand">
            {/* logo 由后端 sys_config 下发(可空);空时回退内置矢量 logo。 */}
            {site.logo ? <img src={site.logo} alt={site.title} className="lf-logo" /> : <TenonLogo size={34} />}
            <span className="lf-word">{site.title}</span>
          </div>
          <h2 className="lf-title">{t('login.title')}</h2>
        </>
      ) : null}

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
            <div className="lf-captcha">
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
                className="lf-captcha-img"
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

      {showFooter ? (
        <footer className="lf-foot">
          <span>
            © {year}{' '}
            {site.copyright ? (
              site.copyrightUrl ? (
                <a href={site.copyrightUrl} target="_blank" rel="noreferrer">
                  {site.copyright}
                </a>
              ) : (
                site.copyright
              )
            ) : (
              site.title
            )}
          </span>
          {appVersion ? <span className="lf-ver">v{appVersion}</span> : null}
        </footer>
      ) : null}
    </div>
  )
}
