import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, waitFor, cleanup, fireEvent } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import { MemoryRouter } from 'react-router-dom'

vi.mock('@/api', async (orig) => {
  const actual = await orig<typeof import('@/api')>()
  return {
    ...actual, // ApiError 要真的(translateError 用 instanceof 判它)
    authApi: {
      login: vi.fn(), captcha: vi.fn(),
      smsLoginSend: vi.fn(), smsLogin: vi.fn(), smsChallengeLogin: vi.fn(), smsChallengeResend: vi.fn(),
    },
    configApi: { siteInfo: vi.fn() },
    // providers 默认空数组(SSO 区不显);不 mock 会走真 fetch 打网络(air-gap 违规)。
    externalAuthApi: { ...actual.externalAuthApi, providers: vi.fn(() => Promise.resolve([])) },
  }
})

const navigate = vi.fn()
vi.mock('react-router-dom', async (orig) => ({
  ...(await orig<typeof import('react-router-dom')>()),
  useNavigate: () => navigate,
}))

// D5 后登录壳/皮肤从 `@ant-design/icons` **barrel** 具名导入(壳 Moon/Sun、双栏 CheckCircle);而 `mount()`
// 的 `vi.resetModules()` 会强制重估整个 barrel(数千图标 > 5s)→ 动态 import 超时。antd 组件内部走深导入
// (`.../es/icons/*`)不碰 barrel,故只 stub 本图用到的这三个具名图标即可解挂,不影响 antd 自身图标。
vi.mock('@ant-design/icons', () => ({
  MoonOutlined: () => null,
  SunOutlined: () => null,
  CheckCircleFilled: () => null,
}))

import { authApi, configApi, externalAuthApi, ApiError } from '@/api'

const loginMock = vi.mocked(authApi.login)
const captchaMock = vi.mocked(authApi.captcha)
const siteMock = vi.mocked(configApi.siteInfo)
const providersMock = vi.mocked(externalAuthApi.providers)
const smsSendMock = vi.mocked(authApi.smsLoginSend)
const smsLoginMock = vi.mocked(authApi.smsLogin)
const challengeLoginMock = vi.mocked(authApi.smsChallengeLogin)
const challengeResendMock = vi.mocked(authApi.smsChallengeResend)

const SITE = {
  title: '榫卯后台', subtitle: '', copyright: '', copyrightUrl: '', logo: '',
  captchaEnabled: false, smsLoginEnabled: false,
}
const SESSION = {
  accessToken: 'A', refreshToken: 'R', userId: 1, account: 'superAdmin', name: '超管', mustChangePassword: false,
  expiresAt: '2099-01-01T00:00:00Z', refreshExpiresAt: '2099-01-01T00:00:00Z',
}

/**
 * `site` store 的在途缓存是模块级的,每条用例都要重新求值整棵图。
 *
 * **返回的是新图里的 user store,不是文件顶部那个静态导入的。** `resetModules` 之后
 * `import('./LoginPage')` 会连带新建一份 `@/stores/user`,页面写的是新的那份,而静态导入的还是旧的
 * —— 断言读旧的必然看到空会话。踩过一次(「成功:落会话」红在 `expected '' to be 'A'`),
 * 与 `api/client.spec.ts` 里那个是同一类:`resetModules` 造出的是**两套并行的模块图**,
 * 跨图读写永远对不上。
 */
async function mount(site = SITE) {
  siteMock.mockResolvedValue(site)
  vi.resetModules()
  const { default: Page } = await import('./LoginPage')
  const { useUserStore: store } = await import('@/stores/user')
  store.setState({ accessToken: '', refreshToken: '', userInfo: null })
  render(
    <MemoryRouter>
      <AntdApp>
        <Page />
      </AntdApp>
    </MemoryRouter>,
  )
  await screen.findByTestId('login-card')
  return store
}

beforeEach(() => {
  cleanup()
  // `mockReset` 而不是 `clearAllMocks`:后者只清调用记录、**留着实现**,于是上一条用例用
  // `mockResolvedValueOnce` 链排队但没消费完的返回值会泄漏到下一条的首次 captcha 调用 ——
  // 表现是一条本来无关的用例在别的变异下莫名变红(踩过:L1 变异让"math 提示语"连带红,
  // 单跑那条却是绿的,判据脆弱不是真信号)。放在 beforeEach 而不是 mount 里:此刻用例还没设自己的返回值。
  captchaMock.mockReset()
  loginMock.mockReset()
  siteMock.mockReset()
  smsSendMock.mockReset()
  smsLoginMock.mockReset()
  challengeLoginMock.mockReset()
  challengeResendMock.mockReset()
  navigate.mockClear() // 它是本文件自己的 vi.fn(),不在上面三个里;漏清会让"失败不跳转"吃到上一条的调用
})

afterEach(() => {
  cleanup()
  vi.resetModules()
})

const submit = () => fireEvent.submit(screen.getByTestId('login-card').querySelector('form')!)

describe('品牌信息来自 site store', () => {
  it('标题用后端下发的值', async () => {
    await mount()
    expect(await screen.findByText('榫卯后台')).toBeTruthy()
  })

  it('logo 为空时不渲染 img(不占位)', async () => {
    await mount()
    expect(screen.getByTestId('login-card').querySelector('img')).toBeNull()
  })

  it('logo 非空时渲染出来', async () => {
    await mount({ ...SITE, logo: '/files/logo.png' })
    await waitFor(() => expect(screen.getByTestId('login-card').querySelector('img')?.getAttribute('src')).toBe('/files/logo.png'))
  })
})

describe('短信免密登录(site.smsLoginEnabled 驱动)', () => {
  it('未启用:不渲染切换链接', async () => {
    await mount()
    expect(screen.queryByText('短信登录')).toBeNull()
  })

  it('启用:切短信模式 → 发码 → 提交走 sms 端点并落会话', async () => {
    smsSendMock.mockResolvedValue({ expiresSeconds: 300, resendSeconds: 60 })
    smsLoginMock.mockResolvedValue(SESSION)
    const store = await mount({ ...SITE, smsLoginEnabled: true })

    fireEvent.click(screen.getByText('短信登录'))
    // 账号/密码字段随模式卸载,手机号字段出现
    expect(screen.queryByPlaceholderText('请输入密码')).toBeNull()
    fireEvent.change(await screen.findByPlaceholderText('请输入手机号'), { target: { value: '13800138000' } })

    fireEvent.click(screen.getByText('获取验证码'))
    await waitFor(() => expect(smsSendMock).toHaveBeenCalledWith(expect.objectContaining({ phone: '13800138000' })))
    // 发码成功进入倒计时,按钮转"Ns 后重发"并禁用
    expect(await screen.findByText(/\d+s 后重发/)).toBeTruthy()

    fireEvent.change(screen.getByPlaceholderText('请输入短信验证码'), { target: { value: '123456' } })
    submit()
    await waitFor(() => expect(smsLoginMock).toHaveBeenCalledWith({ phone: '13800138000', code: '123456' }))
    await waitFor(() => expect(store.getState().accessToken).toBe('A'))
  })

  it('手机号为空点发码:提示且不发请求', async () => {
    await mount({ ...SITE, smsLoginEnabled: true })
    fireEvent.click(screen.getByText('短信登录'))
    fireEvent.click(screen.getByText('获取验证码'))
    expect(await screen.findByText('请输入手机号')).toBeTruthy()
    expect(smsSendMock).not.toHaveBeenCalled()
  })
})

describe('短信二次验证(40009 信令)', () => {
  const reject40009 = () =>
    loginMock.mockRejectedValue(
      new ApiError(40009, 'error.auth.smsChallengeRequired', { challengeId: 'ch1', phoneMask: '138****8000', resendSeconds: 60 }),
    )

  it('密码过后 40009:切挑战页(掩码手机号),提交码完成登录', async () => {
    reject40009()
    challengeLoginMock.mockResolvedValue(SESSION)
    const store = await mount()
    submit()

    expect(await screen.findByText('验证码已发送至 138****8000')).toBeTruthy()
    fireEvent.change(screen.getByPlaceholderText('请输入短信验证码'), { target: { value: '654321' } })
    fireEvent.click(screen.getByRole('button', { name: /登\s*录/ }))
    await waitFor(() => expect(challengeLoginMock).toHaveBeenCalledWith({ challengeId: 'ch1', code: '654321' }))
    await waitFor(() => expect(store.getState().accessToken).toBe('A'))
  })

  it('返回密码登录:回到账号表单', async () => {
    reject40009()
    await mount()
    submit()
    await screen.findByText('验证码已发送至 138****8000')

    fireEvent.click(screen.getByText('返回密码登录'))
    expect(await screen.findByPlaceholderText('请输入账号')).toBeTruthy()
  })

  it('40009 不落会话、不当失败弹错', async () => {
    reject40009()
    const store = await mount()
    submit()
    await screen.findByText('验证码已发送至 138****8000')
    expect(store.getState().accessToken).toBe('')
  })
})

describe('第三方登录(SSO)由后端 providers 驱动', () => {
  it('无启用 provider(默认空数组):整段不渲染', async () => {
    await mount()
    expect(screen.getByTestId('login-card').querySelector('.lf-sso')).toBeNull()
  })

  it('有启用 provider:渲染分隔线 + 按钮行', async () => {
    providersMock.mockResolvedValueOnce([
      { code: 'gitee', displayName: 'Gitee' },
      { code: 'github', displayName: 'GitHub', icon: 'ph:github-logo' },
    ])
    await mount()
    expect(await screen.findByText('GitHub')).toBeTruthy()
    expect(screen.getByText('Gitee')).toBeTruthy()
    expect(screen.getByText('其他登录方式')).toBeTruthy()
    expect(screen.getByTestId('login-card').querySelectorAll('.lf-sso-btn').length).toBe(2)
  })
})

describe('验证码由站点信息运行时驱动', () => {
  it('未启用:不拉图、不渲染验证码框', async () => {
    await mount()
    expect(captchaMock).not.toHaveBeenCalled()
    expect(screen.queryByTestId('captcha-img')).toBeNull()
  })

  it('启用:拉图并渲染', async () => {
    captchaMock.mockResolvedValue({ captchaId: 'c1', svg: '<svg id="s1"/>', type: 'char' })
    await mount({ ...SITE, captchaEnabled: true })
    await waitFor(() => expect(screen.getByTestId('captcha-img').innerHTML).toContain('s1'))
    expect(captchaMock).toHaveBeenCalledTimes(1)
  })

  it('点击图形换一张(票据一次性,看不清时要能重取)', async () => {
    captchaMock
      .mockResolvedValueOnce({ captchaId: 'c1', svg: '<svg id="s1"/>', type: 'char' })
      .mockResolvedValueOnce({ captchaId: 'c2', svg: '<svg id="s2"/>', type: 'char' })
    await mount({ ...SITE, captchaEnabled: true })
    await waitFor(() => expect(screen.getByTestId('captcha-img').innerHTML).toContain('s1'))

    fireEvent.click(screen.getByTestId('captcha-img'))
    await waitFor(() => expect(screen.getByTestId('captcha-img').innerHTML).toContain('s2'))
  })

  /**
   * **验证码票据是一次性的**:用掉之后无论成败,服务端那张都作废了。登录失败若不换图,
   * 用户照着同一张图再输一次必然再失败 —— 症状是「验证码明明输对了还说错」,
   * 而且第二次的错误码与第一次不同(票据失效 vs 密码错误),极难从工单里归因。
   */
  it('登录失败后自动换一张验证码', async () => {
    captchaMock
      .mockResolvedValueOnce({ captchaId: 'c1', svg: '<svg id="s1"/>', type: 'char' })
      .mockResolvedValueOnce({ captchaId: 'c2', svg: '<svg id="s2"/>', type: 'char' })
    loginMock.mockRejectedValue(new ApiError(40004, 'error.auth.passwordWrong'))
    await mount({ ...SITE, captchaEnabled: true })
    await waitFor(() => expect(captchaMock).toHaveBeenCalledTimes(1))

    fireEvent.change(screen.getByPlaceholderText('请输入验证码'), { target: { value: '1234' } })
    submit()

    await waitFor(() => expect(captchaMock).toHaveBeenCalledTimes(2))
    expect(screen.getByTestId('captcha-img').innerHTML).toContain('s2')
  })

  it('math 类型的提示语不同(要算出结果再输)', async () => {
    captchaMock.mockResolvedValue({ captchaId: 'c1', svg: '<svg/>', type: 'math' })
    await mount({ ...SITE, captchaEnabled: true })
    expect(await screen.findByPlaceholderText('请输入计算结果')).toBeTruthy()
  })
})

describe('提交', () => {
  it('成功:落会话 + 跳首页(replace,不留登录页在历史里)', async () => {
    loginMock.mockResolvedValue(SESSION)
    const store = await mount()
    submit()

    await waitFor(() => expect(store.getState().accessToken).toBe('A'))
    expect(navigate).toHaveBeenCalledWith('/', { replace: true })
  })

  it('失败:不落会话、不跳转,错误经 translateError 变成本地化文案', async () => {
    loginMock.mockRejectedValue(new ApiError(40004, 'error.auth.passwordWrong'))
    const store = await mount()
    submit()

    await waitFor(() => expect(loginMock).toHaveBeenCalled())
    expect(await screen.findByText('账号或密码错误')).toBeTruthy() // 而不是 msgKey 原文
    expect(store.getState().accessToken).toBe('')
    expect(navigate).not.toHaveBeenCalled()
  })

  it('必填校验拦在前面:账号为空时根本不发请求', async () => {
    loginMock.mockResolvedValue(SESSION)
    await mount()
    fireEvent.change(screen.getByPlaceholderText('请输入账号'), { target: { value: '' } })
    submit()

    await waitFor(() => expect(screen.getByText('请输入账号和密码')).toBeTruthy())
    expect(loginMock).not.toHaveBeenCalled()
  })
})

// ── F1 登录皮肤外壳:皮肤选择阶梯 / 切换持久化 / showBrand·showFooter / SplitPanel i18n / Spotlight 指针 ──
// 三套皮肤根元素类名互斥(aurora / split / spotlight 各是独立 class token,子元素如 split-panel、
// aurora-bg、spot 都是别的 token),据此判定当前渲染的是哪套。皮肤由 LoginPage.initSkin 选中
// (`?skin=` → localStorage → 默认),故用例在 mount() 前布置 location.search / localStorage。
describe('登录皮肤外壳(F1)', () => {
  beforeEach(() => {
    localStorage.clear()
    window.history.replaceState(null, '', '/')
  })
  afterEach(() => {
    localStorage.clear()
    window.history.replaceState(null, '', '/')
  })

  const activeSkin = () =>
    document.querySelector('.spotlight')
      ? 'spotlight'
      : document.querySelector('.split')
        ? 'split'
        : document.querySelector('.aurora')
          ? 'aurora'
          : null

  describe('皮肤选择阶梯', () => {
    it('默认(无 query、无记忆):渲染默认皮肤 aurora', async () => {
      await mount()
      expect(activeSkin()).toBe('aurora')
    })

    it('?skin= 一次性覆盖:渲染指定皮肤,且不写记忆(预览不落库)', async () => {
      window.history.replaceState(null, '', '/login?skin=split')
      await mount()
      expect(activeSkin()).toBe('split')
      expect(localStorage.getItem('login-skin')).toBeNull()
    })

    it('?skin= 非法值:回退到默认', async () => {
      window.history.replaceState(null, '', '/login?skin=nope')
      await mount()
      expect(activeSkin()).toBe('aurora')
    })

    it('记忆命中(localStorage):无 query 时渲染记住的皮肤', async () => {
      localStorage.setItem('login-skin', 'spotlight')
      await mount()
      expect(activeSkin()).toBe('spotlight')
    })

    it('?skin= 优先于记忆', async () => {
      localStorage.setItem('login-skin', 'spotlight')
      window.history.replaceState(null, '', '/login?skin=split')
      await mount()
      expect(activeSkin()).toBe('split')
    })
  })

  describe('切换 UI + 持久化', () => {
    it('点击皮肤段:切换激活皮肤并写盘', async () => {
      await mount()
      expect(activeSkin()).toBe('aurora')
      fireEvent.click(screen.getByRole('tab', { name: '双栏' }))
      await waitFor(() => expect(activeSkin()).toBe('split'))
      expect(localStorage.getItem('login-skin')).toBe('split')
    })

    it('激活段 aria-selected=true,其余 false', async () => {
      await mount()
      fireEvent.click(screen.getByRole('tab', { name: '聚光' }))
      await waitFor(() => expect(activeSkin()).toBe('spotlight'))
      expect(screen.getByRole('tab', { name: '聚光' }).getAttribute('aria-selected')).toBe('true')
      expect(screen.getByRole('tab', { name: '极光' }).getAttribute('aria-selected')).toBe('false')
    })
  })

  describe('showBrand / showFooter', () => {
    it('双栏皮肤:卡内表单关掉品牌与页脚(左栏自绘,免卡内重复)', async () => {
      localStorage.setItem('login-skin', 'split')
      await mount()
      const card = screen.getByTestId('login-card')
      expect(card.querySelector('.lf-brand')).toBeNull()
      expect(card.querySelector('.lf-foot')).toBeNull()
    })

    it('极光皮肤:卡内表单默认渲染品牌与页脚', async () => {
      await mount()
      const card = screen.getByTestId('login-card')
      expect(card.querySelector('.lf-brand')).not.toBeNull()
      expect(card.querySelector('.lf-foot')).not.toBeNull()
    })
  })

  describe('SplitPanel i18n', () => {
    it('卖点与 headline 走 i18n 真串', async () => {
      localStorage.setItem('login-skin', 'split')
      await mount()
      expect(screen.getByText('RBAC 角色授权')).toBeTruthy()
      expect(screen.getByText('多机构数据范围')).toBeTruthy()
      expect(screen.getByText('多应用门户')).toBeTruthy()
      // headline 三段(pre/accent/post),accent 段独立 span 染色;整段拼齐才对
      expect(screen.getByText('权限')).toBeTruthy()
      expect(document.querySelector('.split-headline')?.textContent).toBe('企业级权限管理后台')
      expect(screen.getByText('欢迎回来')).toBeTruthy()
    })
  })

  describe('Spotlight 指针映射', () => {
    it('指针移动映射到根元素 --mx/--my 百分比(非对称值以防 x/y 互换)', async () => {
      localStorage.setItem('login-skin', 'spotlight')
      await mount()
      const root = document.querySelector('.spotlight') as HTMLElement
      window.innerWidth = 800
      window.innerHeight = 400
      const w = window.innerWidth
      const h = window.innerHeight
      window.dispatchEvent(new MouseEvent('mousemove', { clientX: w * 0.25, clientY: h * 0.75 }))
      expect(root.style.getPropertyValue('--mx')).toBe('25%')
      expect(root.style.getPropertyValue('--my')).toBe('75%')
    })
  })
})
