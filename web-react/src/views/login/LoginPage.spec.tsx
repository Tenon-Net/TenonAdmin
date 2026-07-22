import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, waitFor, cleanup, fireEvent } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import { MemoryRouter } from 'react-router-dom'

vi.mock('@/api', async (orig) => {
  const actual = await orig<typeof import('@/api')>()
  return {
    ...actual, // ApiError 要真的(translateError 用 instanceof 判它)
    authApi: { login: vi.fn(), captcha: vi.fn() },
    configApi: { siteInfo: vi.fn() },
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

import { authApi, configApi, ApiError } from '@/api'

const loginMock = vi.mocked(authApi.login)
const captchaMock = vi.mocked(authApi.captcha)
const siteMock = vi.mocked(configApi.siteInfo)

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
