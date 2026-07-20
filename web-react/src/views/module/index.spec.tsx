import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, waitFor, fireEvent } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import { MemoryRouter } from 'react-router-dom'
import type { AppModule } from '@/types/menu'

const navigate = vi.fn()
vi.mock('react-router-dom', async (orig) => ({
  ...(await orig<typeof import('react-router-dom')>()),
  useNavigate: () => navigate,
}))

// switchModule/setDefault 在 useModule 已单测 + 变异;这里 mock 掉,只验页面**接线**(点了没点对、导航去没去)。
vi.mock('@/composables/useModule', () => ({ switchModule: vi.fn(), setDefault: vi.fn() }))
vi.mock('@/api', async (orig) => ({ ...(await orig<typeof import('@/api')>()), authApi: { logout: vi.fn() } }))

import { switchModule, setDefault } from '@/composables/useModule'
import { authApi } from '@/api'
import { useAuthStore } from '@/stores/auth'
import { useUserStore } from '@/stores/user'
import ModuleChooser from './index'

const switchMock = vi.mocked(switchModule)
const setDefaultMock = vi.mocked(setDefault)
const logoutMock = vi.mocked(authApi.logout)

const mod = (id: number, extra: Partial<AppModule> = {}): AppModule => ({ id, code: `m${id}`, title: `应用${id}`, sort: 0, ...extra })

function mount() {
  render(
    <AntdApp>
      <MemoryRouter>
        <ModuleChooser />
      </MemoryRouter>
    </AntdApp>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  useAuthStore.getState().reset()
  useUserStore.setState({ accessToken: 't', refreshToken: 'r', userInfo: { userId: 1, account: 'a', name: 'n', mustChangePassword: false } })
  switchMock.mockResolvedValue('/home')
  setDefaultMock.mockResolvedValue()
  logoutMock.mockResolvedValue(true)
})

afterEach(cleanup)

describe('ModuleChooser 呈现', () => {
  it('列出所有应用,当前应用有标记、默认应用有角标', () => {
    useAuthStore.setState({ modules: [mod(3), mod(8)], currentModuleId: 3, defaultModuleId: 8 })
    mount()
    expect(screen.getByText('应用3')).toBeTruthy()
    expect(screen.getByText('应用8')).toBeTruthy()
    expect(screen.getByText('默认')).toBeTruthy() // 8 的角标
  })

  it('无应用 → 空态 + 登出入口', () => {
    useAuthStore.setState({ modules: [] })
    mount()
    expect(screen.getByText('暂无可访问的应用,请联系管理员分配')).toBeTruthy()
    expect(screen.getByText('退出登录')).toBeTruthy()
  })

  it('登录直落(routesReady=false)不显返回;应用内进来(routesReady=true)显返回', () => {
    useAuthStore.setState({ modules: [mod(3)], routesReady: false })
    mount()
    expect(screen.queryByText('返回')).toBeNull()
    cleanup()
    useAuthStore.setState({ modules: [mod(3)], routesReady: true })
    mount()
    expect(screen.getByText('返回')).toBeTruthy()
  })
})

describe('ModuleChooser 接线', () => {
  it('点卡片 → switchModule(id) 并导航到它返回的首页', async () => {
    useAuthStore.setState({ modules: [mod(3), mod(8)] })
    switchMock.mockResolvedValue('/app8/home')
    mount()
    fireEvent.click(screen.getByTestId('module-card-8'))
    await waitFor(() => expect(switchMock).toHaveBeenCalledWith(8))
    expect(navigate).toHaveBeenCalledWith('/app8/home', { replace: true })
  })

  it('点"设为默认" → setDefault(id),且不触发进应用(阻止冒泡)', async () => {
    useAuthStore.setState({ modules: [mod(3), mod(8)], defaultModuleId: 3 })
    mount()
    // 8 不是默认,显示"设为默认"按钮
    fireEvent.click(screen.getByText('设为默认'))
    await waitFor(() => expect(setDefaultMock).toHaveBeenCalledWith(8))
    expect(switchMock).not.toHaveBeenCalled() // 关键:设默认没把人带进应用
  })

  it('空态点登出 → 调后端登出 + 清会话 + 跳登录', async () => {
    useAuthStore.setState({ modules: [] })
    mount()
    fireEvent.click(screen.getByText('退出登录'))
    await waitFor(() => expect(useUserStore.getState().accessToken).toBe(''))
    expect(logoutMock).toHaveBeenCalled()
    expect(navigate).toHaveBeenCalledWith('/login', { replace: true })
  })

  it('后端登出失败也照样清会话跳登录(不被 500 卡住)', async () => {
    useAuthStore.setState({ modules: [] })
    logoutMock.mockRejectedValue(new Error('500'))
    mount()
    fireEvent.click(screen.getByText('退出登录'))
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/login', { replace: true }))
    expect(useUserStore.getState().accessToken).toBe('')
  })
})
