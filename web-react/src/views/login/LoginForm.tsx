import { useEffect, useMemo, useRef, useState } from 'react'
import { Alert, App, Button, Checkbox, Dropdown, Form, Input, Modal } from 'antd'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { authApi, externalAuthApi, ApiError, type ExternalProvider } from '@/api'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'
import { useSiteStore, appVersion } from '@/stores/site'
import { translateError } from '@/utils/error'
import {
  splitLoginProviders,
  PREVIEW_ALL_SSO_BRANDS,
  previewAllBrandProviders,
} from '@/utils/oauthBrand'
import { TenonLogo } from '@/components/TenonLogo'
import { AppIcon } from '@/components/AppIcon'
import { BrandIcon } from '@/components/oauth/BrandIcon'
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
 * + 密码后短信二次验证(40009) / TOTP 挑战(40018) / 强制 MFA 未绑定引导(40020)。
 * 登录页默认不常驻「设置身份验证器」;自愿绑定走个人中心。
 */
export function LoginForm({ showBrand = true, showFooter = true }: { showBrand?: boolean; showFooter?: boolean }) {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const site = useSiteStore((s) => s.site)
  const loadSite = useSiteStore((s) => s.load)
  const setSession = useUserStore((s) => s.setSession)
  const year = new Date().getFullYear()

  const pendingLink = searchParams.get('pendingLink') ?? ''
  const pendingProvider = searchParams.get('provider') ?? ''
  const pendingDisplayName = searchParams.get('displayName') ?? ''

  const [loading, setLoading] = useState(false)
  const [pendingConfirmOpen, setPendingConfirmOpen] = useState(false)
  const [pendingClaimToken, setPendingClaimToken] = useState('')
  const [pendingClaimBusy, setPendingClaimBusy] = useState(false)
  const [captcha, setCaptcha] = useState<{ id: string; svg: string; type: string } | null>(null)
  // 第三方登录:默认后端 providers;PREVIEW_ALL_SSO_BRANDS 时铺全品牌图(图标验收)。
  const [ssoProviders, setSsoProviders] = useState<ExternalProvider[]>([])
  const ssoDisplayList = useMemo(
    () => (PREVIEW_ALL_SSO_BRANDS ? previewAllBrandProviders() : ssoProviders),
    [ssoProviders],
  )
  const ssoSplit = useMemo(
    () =>
      PREVIEW_ALL_SSO_BRANDS
        ? { visible: ssoDisplayList, overflow: [] as typeof ssoDisplayList }
        : splitLoginProviders(ssoDisplayList),
    [ssoDisplayList],
  )
  const pendingProviderLabel = useMemo(() => {
    if (!pendingProvider) return t('oauth.thirdParty')
    const hit = ssoProviders.find((p) => p.code === pendingProvider)
    return hit?.displayName || pendingProvider
  }, [pendingProvider, ssoProviders, t])
  const [form] = Form.useForm<FormValues>()

  // 登录形态:账号密码 / 短信免密 / 短信二次验证(40009) / TOTP 二次验证(40018)
  // 强制 MFA 未绑定(40020)用 Modal 引导,不切 mode
  const [mode, setMode] = useState<'account' | 'sms' | 'mfa' | 'totp'>('account')
  // 二次验证挑战(40009 信令 args 下发);码输入是单字段小表单,走受控 state 不进 antd Form
  const [mfa, setMfa] = useState({ challengeId: '', phoneMask: '' })
  const [mfaCode, setMfaCode] = useState('')
  const [totp, setTotp] = useState({ challengeId: '' })
  const [totpCode, setTotpCode] = useState('')
  // 强制 MFA 但未绑定(40020):弹窗 + 带到 /mfa/bind 的账号
  const [bindRequiredOpen, setBindRequiredOpen] = useState(false)
  const [bindAccount, setBindAccount] = useState('')

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

    // SSO 回调带回的 TOTP 挑战
    const ch = new URLSearchParams(window.location.search).get('totpChallenge')
    if (ch) {
      setTotp({ challengeId: ch })
      setTotpCode('')
      setMode('totp')
    }
  }, [loadSite])

  /** 点击第三方登录:顶层导航到 authorize,后端 302 跳 IdP(OAuth2 授权码往返)。 */
  /** 顶层跳 IdP(与 Gitee 一样用 <a href>) */
  function ssoHref(code: string) {
    return externalAuthApi.authorizeUrl(code)
  }

  /** 验证码一次性消费:任何用掉票据的请求失败后必刷新,避免复用作废票据。 */
  async function refreshCaptchaAfterUse() {
    if (site.captchaEnabled) {
      form.setFieldValue('captchaCode', '')
      await loadCaptcha()
    }
  }

  async function finishLogin(res: Awaited<ReturnType<typeof authApi.login>>) {
    // 每次登录清授权态,否则 routesReady 残留 true 会跳过 enterInitial → 空菜单/全 404。
    useAuthStore.getState().reset()
    setSession(res)

    // 现场绑定:不静默 claim——须用户确认,并依赖服务端 binder cookie 同浏览器校验
    if (pendingLink) {
      setPendingClaimToken(pendingLink)
      setPendingConfirmOpen(true)
      return
    }
    message.success(t('login.success'))
    void navigate('/', { replace: true })
  }

  async function confirmPendingBind() {
    if (pendingClaimBusy) return
    setPendingClaimBusy(true)
    try {
      await externalAuthApi.claimPendingLink(pendingClaimToken)
      message.success(t('oauth.pendingLinkSuccess', { name: pendingProviderLabel }))
    } catch (e) {
      message.warning(translateError(e))
    } finally {
      setPendingClaimBusy(false)
      setPendingConfirmOpen(false)
      setPendingClaimToken('')
    }
    void navigate('/', { replace: true })
  }

  function skipPendingBind() {
    setPendingConfirmOpen(false)
    setPendingClaimToken('')
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
      await finishLogin(res)
    } catch (err) {
      if (err instanceof ApiError && err.code === 40018 && err.args) {
        setTotp({ challengeId: String(err.args.challengeId ?? '') })
        setTotpCode('')
        setMode('totp')
      } else if (err instanceof ApiError && err.code === 40020) {
        // 40020 = 强制 MFA 未绑定 → Modal 引导自助设置(登录页默认不常驻链接)
        const account = String(values.account ?? '').trim()
        setBindAccount(account)
        setBindRequiredOpen(true)
      } else if (err instanceof ApiError && err.code === 40009 && err.args) {
        // 40009 = 密码已过、需短信二次验证
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

  function goBindAuthenticator() {
    const account = bindAccount || String(form.getFieldValue('account') ?? '').trim()
    setBindRequiredOpen(false)
    void navigate(account ? `/mfa/bind?account=${encodeURIComponent(account)}` : '/mfa/bind')
  }

  function goRecovery() {
    const account = String(form.getFieldValue('account') ?? '').trim() || bindAccount
    const q = new URLSearchParams({ mode: 'recovery' })
    if (account) q.set('account', account)
    void navigate(`/mfa/bind?${q.toString()}`)
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
      await finishLogin(await authApi.smsChallengeLogin({ challengeId: mfa.challengeId, code: mfaCode }))
    } catch (err) {
      message.error(translateError(err))
    } finally {
      setLoading(false)
    }
  }

  async function onTotpSubmit() {
    if (!totpCode) {
      message.warning(t('login.totpPlaceholder'))
      return
    }
    setLoading(true)
    try {
      await finishLogin(await authApi.totpChallengeLogin({ challengeId: totp.challengeId, code: totpCode }))
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
    setTotp({ challengeId: '' })
    setTotpCode('')
  }

  const account = useUserStore((s) => s.userInfo?.account ?? '')
  const pendingIdentity = pendingDisplayName
    ? t('oauth.pendingLinkIdentity', { display: pendingDisplayName })
    : ''

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

      {pendingLink ? (
        <Alert
          type="info"
          showIcon
          className="lf-pending-alert"
          style={{ marginBottom: 16, borderRadius: 10, textAlign: 'left' }}
          message={t('oauth.pendingLinkTitle', { name: pendingProviderLabel })}
          description={t('oauth.pendingLinkHint', { name: pendingProviderLabel })}
        />
      ) : null}

      <Modal
        open={pendingConfirmOpen}
        title={t('oauth.pendingLinkConfirmTitle')}
        okText={t('oauth.pendingLinkConfirmOk')}
        cancelText={t('oauth.pendingLinkConfirmSkip')}
        confirmLoading={pendingClaimBusy}
        closable={false}
        maskClosable={false}
        onOk={() => void confirmPendingBind()}
        onCancel={skipPendingBind}
      >
        <p>
          {t('oauth.pendingLinkConfirmContent', {
            name: pendingProviderLabel,
            identity: pendingIdentity,
            account,
          })}
        </p>
      </Modal>

      {/* 短信/TOTP 二次验证态:标题换挑战提示;其余态显常规标题(随 showBrand) */}
      {mode === 'mfa' ? (
        <>
          <h2 className="lf-title">{t('login.mfaTitle', { phone: mfa.phoneMask })}</h2>
          <p className="lf-hint-line">{t('login.mfaSub')}</p>
        </>
      ) : mode === 'totp' ? (
        <>
          <h2 className="lf-title">{t('login.totpTitle')}</h2>
          <p className="lf-hint-line">{t('login.totpSub')}</p>
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
      ) : mode === 'totp' ? (
        <>
          <Input
            size="large"
            maxLength={6}
            value={totpCode}
            onChange={(e) => setTotpCode(e.target.value)}
            placeholder={t('login.totpPlaceholder')}
            onPressEnter={() => void onTotpSubmit()}
          />
          <div className="row lf-between">
            <a className="lf-link" onClick={backToAccount}>
              {t('login.backToPassword')}
            </a>
            <a className="lf-link" onClick={goRecovery}>
              {t('login.useRecovery')}
            </a>
          </div>
          <Button type="primary" size="large" block loading={loading} onClick={() => void onTotpSubmit()}>
            {t('login.submit')}
          </Button>
        </>
      ) : (
        <>
          <Form<FormValues>
            form={form}
            layout="vertical"
            requiredMark={false}
            // ponytail: 开发环境预填超管;pending-link 不预填密码,避免「一点就进」被当成 SSO 直登。
            initialValues={
              import.meta.env.DEV
                ? {
                    account: 'superAdmin',
                    password: pendingLink ? '' : 'Aa123456',
                    remember: true,
                  }
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

          {/* 第三方登录:Gitee 风圆标。PREVIEW_ALL_SSO_BRANDS 时展示全部品牌图。 */}
          {ssoDisplayList.length > 0 ? (
            <>
              <div className="lf-divider">
                <span>{t('login.otherMethods')}</span>
              </div>
              <div className="lf-sso">
                {ssoSplit.visible.map((p) => (
                  <a
                    key={p.code}
                    className="lf-sso-btn"
                    href={ssoHref(p.code)}
                    title={p.displayName}
                    aria-label={p.displayName}
                  >
                    <BrandIcon code={p.code} icon={p.icon} size={32} />
                  </a>
                ))}
                {ssoSplit.overflow.length > 0 ? (
                  <Dropdown
                    menu={{
                      items: ssoSplit.overflow.map((p) => ({
                        key: p.code,
                        label: (
                          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
                            <BrandIcon code={p.code} icon={p.icon} size={20} />
                            {p.displayName}
                          </span>
                        ),
                        onClick: () => {
                          window.location.href = ssoHref(p.code)
                        },
                      })),
                    }}
                    trigger={['click']}
                  >
                    <a
                      className="lf-sso-btn lf-sso-more"
                      href="#"
                      role="button"
                      title={t('login.moreMethods')}
                      aria-label={t('login.moreMethods')}
                      onClick={(e) => e.preventDefault()}
                    >
                      <AppIcon icon="ph:dots-three-bold" size={18} />
                    </a>
                  </Dropdown>
                ) : null}
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

      {/* 强制 MFA 未绑定(40020):遮罩 Modal,账密表单仍在底下;默认登录页不常驻绑定链接 */}
      <Modal
        open={bindRequiredOpen}
        title={t('login.totpBindTitle')}
        okText={t('login.setupAuthenticator')}
        cancelText={t('common.cancel')}
        onOk={goBindAuthenticator}
        onCancel={() => setBindRequiredOpen(false)}
        destroyOnHidden
      >
        <p style={{ margin: 0 }}>{t('login.totpBindSub')}</p>
      </Modal>
    </div>
  )
}
