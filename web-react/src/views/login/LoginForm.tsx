import { useEffect, useRef, useState } from 'react'
import { App, Button, Checkbox, Form, Input } from 'antd'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { authApi, externalAuthApi, ApiError, type ExternalProvider } from '@/api'
import { useUserStore } from '@/stores/user'
import { useSiteStore, appVersion } from '@/stores/site'
import { translateError } from '@/utils/error'
import { TenonLogo } from '@/components/TenonLogo'
import { AppIcon } from '@/components/AppIcon'
import './loginform.css'

interface FormValues {
  account: string
  password: string
  phone?: string
  smsCode?: string
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
 * 含第三方登录(SSO)按钮区与短信路径:短信免密登录(`site.smsLoginEnabled` 运行时驱动)
 * + 密码后短信二次验证(后端 40009 信令进入,args 带挑战参数)。与 Vue 侧三模式同构。
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
  // 第三方登录:后端 GET providers 驱动(仅点亮已启用的);空数组 = 整段 SSO 区不显。
  const [ssoProviders, setSsoProviders] = useState<ExternalProvider[]>([])
  const [form] = Form.useForm<FormValues>()

  // 登录形态:账号密码 / 短信免密(site.smsLoginEnabled 运行时驱动)/ 短信二次验证(密码过后 40009 信令进入)
  const [mode, setMode] = useState<'account' | 'sms' | 'mfa'>('account')
  // 二次验证挑战(40009 信令 args 下发);码输入是单字段小表单,走受控 state 不进 antd Form
  const [mfa, setMfa] = useState({ challengeId: '', phoneMask: '' })
  const [mfaCode, setMfaCode] = useState('')

  // 发码/重发共用倒计时(同一时刻只有一个发码入口可见)
  const [countdown, setCountdown] = useState(0)
  const timerRef = useRef<number | undefined>(undefined)
  const startCountdown = (s: number) => {
    window.clearInterval(timerRef.current)
    setCountdown(s)
    timerRef.current = window.setInterval(() => {
      setCountdown((c) => {
        if (c <= 1) window.clearInterval(timerRef.current)
        return c - 1
      })
    }, 1000)
  }
  useEffect(() => () => window.clearInterval(timerRef.current), [])

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
    // 未配置外部登录或拉取失败:静默,整段 SSO 区不显
    externalAuthApi.providers().then(setSsoProviders).catch(() => {})
  }, [loadSite])

  /** 点击第三方登录:顶层导航到 authorize,后端 302 跳 IdP(OAuth2 授权码往返)。 */
  function onSso(code: string) {
    window.location.href = externalAuthApi.authorizeUrl(code)
  }

  /** 验证码一次性消费:任何用掉票据的请求失败后必刷新,避免复用作废票据。 */
  async function refreshCaptchaAfterUse() {
    if (site.captchaEnabled) {
      form.setFieldValue('captchaCode', '')
      await loadCaptcha()
    }
  }

  function finishLogin(res: Awaited<ReturnType<typeof authApi.login>>) {
    setSession(res)
    message.success(t('login.success'))
    void navigate('/', { replace: true })
  }

  async function onFinish(values: FormValues) {
    setLoading(true)
    try {
      const res =
        mode === 'sms'
          ? await authApi.smsLogin({ phone: values.phone!, code: values.smsCode! })
          : await authApi.login({
              account: values.account,
              password: values.password,
              captchaId: captcha?.id,
              captchaCode: values.captchaCode,
            })
      finishLogin(res)
    } catch (err) {
      // 40009 = 密码已过、需短信二次验证(信令而非失败):切码输入页,args 带挑战与倒计时参数
      if (err instanceof ApiError && err.code === 40009 && err.args) {
        setMfa({ challengeId: String(err.args.challengeId ?? ''), phoneMask: String(err.args.phoneMask ?? '') })
        setMfaCode('')
        setMode('mfa')
        startCountdown(Number(err.args.resendSeconds ?? 60))
      } else {
        message.error(translateError(err))
      }
      await refreshCaptchaAfterUse()
    } finally {
      setLoading(false)
    }
  }

  /** 短信免密:发码。护发码的验证码在此消费(账号模式的验证码护登录本身)。 */
  async function onSendSmsCode() {
    if (countdown > 0) return
    const phone = form.getFieldValue('phone') as string | undefined
    if (!phone) {
      message.warning(t('login.phonePlaceholder'))
      return
    }
    const captchaCode = form.getFieldValue('captchaCode') as string | undefined
    if (site.captchaEnabled && !captchaCode) {
      message.warning(t('login.captchaRequired'))
      return
    }
    try {
      const r = await authApi.smsLoginSend({ phone, captchaId: captcha?.id, captchaCode })
      message.success(t('login.smsSent'))
      startCountdown(r.resendSeconds)
    } catch (err) {
      message.error(translateError(err))
    }
    await refreshCaptchaAfterUse()
  }

  async function onMfaSubmit() {
    if (!mfaCode) {
      message.warning(t('login.smsCodePlaceholder'))
      return
    }
    setLoading(true)
    try {
      finishLogin(await authApi.smsChallengeLogin({ challengeId: mfa.challengeId, code: mfaCode }))
    } catch (err) {
      message.error(translateError(err))
    } finally {
      setLoading(false)
    }
  }

  async function onMfaResend() {
    if (countdown > 0) return
    try {
      const r = await authApi.smsChallengeResend({ challengeId: mfa.challengeId })
      message.success(t('login.smsSent'))
      startCountdown(r.resendSeconds)
    } catch (err) {
      message.error(translateError(err))
    }
  }

  function backToAccount() {
    setMode('account')
    setMfa({ challengeId: '', phoneMask: '' })
    setMfaCode('')
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
        </>
      ) : null}

      {/* 短信二次验证态:标题换挑战提示(掩码手机号);其余两态显常规标题(随 showBrand) */}
      {mode === 'mfa' ? (
        <>
          <h2 className="lf-title">{t('login.mfaTitle', { phone: mfa.phoneMask })}</h2>
          <p className="lf-hint-line">{t('login.mfaSub')}</p>
        </>
      ) : showBrand ? (
        <h2 className="lf-title">{t('login.title')}</h2>
      ) : null}

      {mode === 'mfa' ? (
        <>
          {/* 短信二次验证:密码已过,凭挑战 + 短信码完成登录。单字段小表单,受控 state 即可 */}
          <Input
            size="large"
            maxLength={6}
            value={mfaCode}
            onChange={(e) => setMfaCode(e.target.value)}
            placeholder={t('login.smsCodePlaceholder')}
            onPressEnter={() => void onMfaSubmit()}
          />
          <div className="row lf-between">
            <a className="lf-link" onClick={backToAccount}>
              {t('login.backToPassword')}
            </a>
            <a className={`lf-link${countdown > 0 ? ' lf-link-disabled' : ''}`} onClick={() => void onMfaResend()}>
              {countdown > 0 ? t('login.resendAfter', { s: countdown }) : t('login.sendCode')}
            </a>
          </div>
          <Button type="primary" size="large" block loading={loading} onClick={() => void onMfaSubmit()}>
            {t('login.submit')}
          </Button>
        </>
      ) : (
        <>
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
            {mode === 'account' ? (
              <>
                <Form.Item name="account" label={t('login.account')} rules={[{ required: true, message: t('login.required') }]}>
                  <Input size="large" autoComplete="username" placeholder={t('login.accountPlaceholder')} />
                </Form.Item>

                <Form.Item name="password" label={t('login.password')} rules={[{ required: true, message: t('login.required') }]}>
                  <Input.Password size="large" autoComplete="current-password" placeholder={t('login.passwordPlaceholder')} />
                </Form.Item>
              </>
            ) : (
              <Form.Item name="phone" label={t('login.phone')} rules={[{ required: true, message: t('login.phonePlaceholder') }]}>
                <Input size="large" autoComplete="tel" maxLength={20} placeholder={t('login.phonePlaceholder')} />
              </Form.Item>
            )}

            {site.captchaEnabled ? (
              <Form.Item label={t('login.captcha')} required>
                <div className="lf-captcha">
                  {/* 必填规则仅账号态:账号模式验证码护登录本身;短信模式护的是发码,由 onSendSmsCode 前置校验,
                      发码后 refreshCaptchaAfterUse 会清空字段,提交时不能再拦。 */}
                  <Form.Item
                    name="captchaCode"
                    noStyle
                    rules={mode === 'account' ? [{ required: true, message: t('login.captchaRequired') }] : []}
                  >
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

            {mode === 'sms' ? (
              <Form.Item label={t('login.smsCode')} required>
                <div className="lf-captcha">
                  <Form.Item name="smsCode" noStyle rules={[{ required: true, message: t('login.smsCodePlaceholder') }]}>
                    <Input size="large" maxLength={6} placeholder={t('login.smsCodePlaceholder')} />
                  </Form.Item>
                  <button type="button" className="lf-send-btn" disabled={countdown > 0} onClick={() => void onSendSmsCode()}>
                    {countdown > 0 ? t('login.resendAfter', { s: countdown }) : t('login.sendCode')}
                  </button>
                </div>
              </Form.Item>
            ) : null}

            {/* 左:记住我(仅账号态);右:账号↔短信切换(后端 smsLoginEnabled 驱动,关则整链不显) */}
            <div className="row lf-between">
              {mode === 'account' ? (
                <Form.Item name="remember" valuePropName="checked" noStyle>
                  <Checkbox>{t('login.remember')}</Checkbox>
                </Form.Item>
              ) : (
                <span />
              )}
              {site.smsLoginEnabled ? (
                <a className="lf-link" onClick={() => setMode(mode === 'account' ? 'sms' : 'account')}>
                  {mode === 'account' ? t('login.smsLogin') : t('login.accountLogin')}
                </a>
              ) : null}
            </div>

            <Button type="primary" size="large" block htmlType="submit" loading={loading}>
              {t('login.submit')}
            </Button>
          </Form>

          {/* 第三方登录:后端 providers 驱动;无启用项则整段不显。 */}
          {ssoProviders.length > 0 ? (
            <>
              <div className="lf-divider">
                <span>{t('login.otherMethods')}</span>
              </div>
              <div className="lf-sso">
                {ssoProviders.map((p) => (
                  <button key={p.code} className="lf-sso-btn" type="button" onClick={() => onSso(p.code)}>
                    {p.icon ? <AppIcon icon={p.icon} size={18} className="lf-sso-icon" /> : null}
                    {p.displayName}
                  </button>
                ))}
              </div>
            </>
          ) : null}
        </>
      )}

      {showFooter ? (
        <footer className="lf-foot">
          {/* 以 copyrightUrl 为门(对齐 Vue LoginForm.vue 与兄弟皮肤 SplitPanel):配了链接就带链接,文案回退 title。 */}
          <span>
            © {year}{' '}
            {site.copyrightUrl ? (
              <a href={site.copyrightUrl} target="_blank" rel="noreferrer">
                {site.copyright || site.title}
              </a>
            ) : (
              site.copyright || site.title
            )}
          </span>
          {appVersion ? <span className="lf-ver">v{appVersion}</span> : null}
        </footer>
      ) : null}
    </div>
  )
}
