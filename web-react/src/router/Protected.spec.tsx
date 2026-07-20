import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { StrictMode } from 'react'
import { render, screen, cleanup, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import type { MenuNode } from '@/types/menu'

vi.mock('@/api', () => ({
  personalApi: { modules: vi.fn(), menu: vi.fn(), permissions: vi.fn(), profile: vi.fn() },
}))
// 动态路由的 glob 换成假表,让 system/user 有落点。
vi.mock('./buildRoutes', async (orig) => {
  const actual = await orig<typeof import('./buildRoutes')>()
  const glob = {
    '/src/views/system/user/index.tsx': async () => ({ default: () => <div>USER PAGE</div> }),
  }
  return { ...actual, buildRoutes: (tree: MenuNode[]) => actual.buildRoutes(tree, glob) }
})

import { personalApi } from '@/api'
import { Protected } from './Protected'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'

const modulesMock = vi.mocked(personalApi.modules)
const menuMock = vi.mocked(personalApi.menu)
const permsMock = vi.mocked(personalApi.permissions)
const profileMock = vi.mocked(personalApi.profile)

const USER_MENU: MenuNode[] = [
  { id: 1, parentId: 0, type: 2, title: '用户', path: 'system/user', component: 'system/user/index', sort: 0, visible: true, children: [] },
]

function mountAt(path: string) {
  render(
    <AntdApp>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/login" element={<div>LOGIN PAGE</div>} />
          <Route path="/*" element={<Protected />} />
        </Routes>
      </MemoryRouter>
    </AntdApp>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  useAuthStore.getState().reset()
  useUserStore.setState({ accessToken: '', refreshToken: '', userInfo: null })
  permsMock.mockResolvedValue([])
  profileMock.mockResolvedValue({ id: 1, account: 'a', name: 'n', isSuperAdmin: false, avatar: null } as never)
  menuMock.mockResolvedValue(USER_MENU)
  modulesMock.mockResolvedValue({ modules: [{ id: 9, code: 'm', title: 'M', sort: 0 }], defaultModuleId: null })
})

afterEach(cleanup)

const login = () => useUserStore.setState({ accessToken: 't', refreshToken: 'r', userInfo: { userId: 1, account: 'a', name: 'n', mustChangePassword: false } })

describe('守卫①:未登录', () => {
  it('未登录访问任意受保护路径 → 跳登录', async () => {
    mountAt('/system/user')
    expect(await screen.findByText('LOGIN PAGE')).toBeTruthy()
    expect(modulesMock).not.toHaveBeenCalled() // 没登录就不拉门户
  })
})

describe('守卫②:强制改密', () => {
  it('mustChangePassword → 锁死改密页,访问别处也被弹过去', async () => {
    useUserStore.setState({ accessToken: 't', refreshToken: 'r', userInfo: { userId: 1, account: 'a', name: 'n', mustChangePassword: true } })
    mountAt('/system/user')
    // UnderConstruction 显示当前路径,应是 /personal/password
    await waitFor(() => expect(screen.getByText('/personal/password')).toBeTruthy())
    expect(modulesMock).not.toHaveBeenCalled() // 改密守卫在门户重建之前,不拉门户
  })
})

describe('守卫③:F5 深链 → enterInitial 重建后渲染目标页', () => {
  it('登录态 + routesReady=false + 深链 /system/user → 重建路由并渲染该页', async () => {
    login()
    mountAt('/system/user')
    // 单应用 → enterInitial 直接进 → 拉菜单 → 动态路由建成 → 命中 USER PAGE
    expect(await screen.findByText('USER PAGE')).toBeTruthy()
    expect(modulesMock).toHaveBeenCalledOnce()
    expect(menuMock).toHaveBeenCalledWith(9)
  })

  it('重建后无权限路径 → 404(而不是白屏或错显登录)', async () => {
    login()
    mountAt('/nope/path')
    expect(await screen.findByText(/没有这个页面/)).toBeTruthy()
  })

  it('多应用无记住无默认(chooser)→ 落 /module 选择器', async () => {
    login()
    modulesMock.mockResolvedValue({ modules: [{ id: 3, code: 'a', title: 'A', sort: 0 }, { id: 8, code: 'b', title: 'B', sort: 0 }], defaultModuleId: null })
    mountAt('/')
    // chooser:menuTree 空 → homePath 回落 /module → /module 渲染占位选择器
    await waitFor(() => expect(screen.getByText('/module')).toBeTruthy())
    expect(menuMock).not.toHaveBeenCalled() // chooser 态没进任何应用
  })

  it('enterInitial 失败 → 清会话 → 跳登录', async () => {
    login()
    modulesMock.mockRejectedValue(new Error('boom'))
    mountAt('/system/user')
    expect(await screen.findByText('LOGIN PAGE')).toBeTruthy()
    expect(useUserStore.getState().accessToken).toBe('') // 会话被清
  })

  it('StrictMode 下 enterInitial 只真拉一次门户(在途去重)', async () => {
    // StrictMode 会 mount→unmount→remount,守卫的 effect 因此跑两次,booted 在 remount 时仍 false。
    // 没有 `enterInitial` 的在途去重,modules/permissions/profile/menu 会各拉两次。
    // 这条**真的套 StrictMode**——上面那条深链测试的 `toHaveBeenCalledOnce` 只因 harness 没套
    // StrictMode 才成立,不代表运行时(main.tsx 用了 StrictMode)。去重让两处一致。
    login()
    render(
      <StrictMode>
        <AntdApp>
          <MemoryRouter initialEntries={['/system/user']}>
            <Routes>
              <Route path="/login" element={<div>LOGIN PAGE</div>} />
              <Route path="/*" element={<Protected />} />
            </Routes>
          </MemoryRouter>
        </AntdApp>
      </StrictMode>,
    )
    expect(await screen.findByText('USER PAGE')).toBeTruthy()
    expect(modulesMock).toHaveBeenCalledOnce() // 双跑合流到一次
    expect(menuMock).toHaveBeenCalledOnce()
  })
})
