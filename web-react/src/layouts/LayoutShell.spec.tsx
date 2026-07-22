import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, waitFor, fireEvent, within } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import type { MenuNode } from '@/types/menu'

const navigate = vi.fn()
vi.mock('react-router-dom', async (orig) => ({
  ...(await orig<typeof import('react-router-dom')>()),
  useNavigate: () => navigate,
}))
vi.mock('@/api', async (orig) => ({ ...(await orig<typeof import('@/api')>()), authApi: { logout: vi.fn() } }))

import '@/locales' // 副作用:初始化 i18n,否则 t('app.collapse') 返回 key 'app.collapse' 而非"折叠菜单"
import { authApi } from '@/api'
import { LayoutShell } from './LayoutShell'
import { useAppStore } from '@/stores/app'
import { useAuthStore } from '@/stores/auth'
import { useUserStore } from '@/stores/user'

const logoutMock = vi.mocked(authApi.logout)

const TREE: MenuNode[] = [
  { id: 1, parentId: 0, type: 2, title: '工作台', path: '/dashboard', sort: 0, visible: true, children: [] },
  {
    id: 2, parentId: 0, type: 1, title: '系统', path: '', sort: 1, visible: true,
    children: [{ id: 3, parentId: 2, type: 2, title: '用户', path: '/system/user', sort: 0, visible: true, children: [] }],
  },
  { id: 4, parentId: 0, type: 2, title: '外部文档', path: 'https://ex.com/docs', sort: 2, visible: true, children: [] },
]

function mountAt(path: string) {
  render(
    <AntdApp>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/login" element={<div>LOGIN PAGE</div>} />
          <Route element={<LayoutShell />}>
            <Route path="/dashboard" element={<div>DASH PAGE</div>} />
            <Route path="/system/user" element={<div>USER PAGE</div>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </AntdApp>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  useAppStore.setState({ collapsed: false, themeScheme: 'light', systemDark: false, layoutMode: 'vertical' })
  useAuthStore.getState().reset()
  useAuthStore.setState({ menuTree: TREE, routesReady: true })
  useUserStore.setState({ accessToken: 't', refreshToken: 'r', userInfo: { userId: 1, account: 'a', name: 'n', mustChangePassword: false } })
  logoutMock.mockResolvedValue(true)
})

afterEach(cleanup)

// 布局壳已从 antd Layout/Sider 改为 CSS-grid 自建(D2a):vertical 模式侧栏是 .shell-aside(内含 SideNav→Menu)。
const sider = () => document.querySelector('.shell-aside') as HTMLElement

describe('LayoutShell 菜单', () => {
  it('渲染菜单项(含目录),内容区渲染当前页', async () => {
    mountAt('/dashboard')
    expect(await screen.findByText('DASH PAGE')).toBeTruthy() // Outlet 渲染
    expect(within(sider()).getByText('工作台')).toBeTruthy()
    expect(within(sider()).getByText('系统')).toBeTruthy() // 目录
    expect(within(sider()).getByText('外部文档')).toBeTruthy() // 外链也在菜单里
  })

  it('选中态跟随路由:在 /system/user 时该项高亮', async () => {
    mountAt('/system/user')
    await screen.findByText('USER PAGE')
    // antd 选中项带 ant-menu-item-selected;它的文本是"用户"
    const selected = sider().querySelector('.ant-menu-item-selected')
    expect(selected?.textContent).toContain('用户')
  })

  it('点内部菜单项 → navigate 到它的 path', async () => {
    mountAt('/dashboard')
    await screen.findByText('DASH PAGE')
    fireEvent.click(within(sider()).getByText('工作台'))
    expect(navigate).toHaveBeenCalledWith('/dashboard')
  })

  it('点外链菜单项 → window.open,不 navigate', async () => {
    const open = vi.fn()
    vi.stubGlobal('open', open) // window.open
    mountAt('/dashboard')
    await screen.findByText('DASH PAGE')
    fireEvent.click(within(sider()).getByText('外部文档'))
    expect(open).toHaveBeenCalledWith('https://ex.com/docs', '_blank', 'noopener,noreferrer')
    expect(navigate).not.toHaveBeenCalled()
    vi.unstubAllGlobals()
  })
})

describe('LayoutShell 布局模式(D2a)', () => {
  it('horizontal:无侧栏,壳带 mode-horizontal,顶栏出菜单容器', async () => {
    useAppStore.setState({ layoutMode: 'horizontal' })
    mountAt('/dashboard')
    await screen.findByText('DASH PAGE')
    expect(document.querySelector('.shell.mode-horizontal')).toBeTruthy()
    expect(document.querySelector('.shell-aside')).toBeNull() // horizontal 无静态侧栏
    expect(document.querySelector('.shell-header-center')).toBeTruthy() // 顶栏菜单容器在
  })

  it('vertical-mix:侧栏是 rail + 二级列,二级列随选中一级出子菜单', async () => {
    useAppStore.setState({ layoutMode: 'vertical-mix' })
    mountAt('/system/user')
    await screen.findByText('USER PAGE')
    const aside = document.querySelector('.shell-aside--mix')
    expect(aside).toBeTruthy()
    expect(aside!.querySelector('.sidenav.rail')).toBeTruthy() // 一级 icon rail
    const l2 = aside!.querySelector('.sidenav:not(.rail)') as HTMLElement // 二级列
    expect(within(l2).getByText('用户')).toBeTruthy() // selectedL1=系统 → 二级出"用户"
  })

  it('top-hybrid-sidebar-first:侧栏只放一级 rail,顶栏出二级菜单', async () => {
    useAppStore.setState({ layoutMode: 'top-hybrid-sidebar-first' })
    mountAt('/system/user')
    await screen.findByText('USER PAGE')
    expect(document.querySelector('.shell.mode-top-hybrid-sidebar-first')).toBeTruthy()
    expect(document.querySelector('.shell-aside .sidenav.rail')).toBeTruthy() // 侧栏是一级 rail
    expect(document.querySelector('.shell-header-center')).toBeTruthy() // 二级在顶栏
  })
})

describe('LayoutShell 设置抽屉 + 面包屑(D2b)', () => {
  it('点齿轮 → 打开设置抽屉(出现外观 tab)', async () => {
    mountAt('/dashboard')
    await screen.findByText('DASH PAGE')
    fireEvent.click(screen.getByLabelText('系统设置')) // 顶栏齿轮
    expect(await screen.findByText('外观')).toBeTruthy() // 抽屉里的外观 tab
  })

  it('showBreadcrumb 时顶栏出面包屑(vertical),链路 = 系统 › 用户', async () => {
    useAppStore.setState({ showBreadcrumb: true, layoutMode: 'vertical' })
    mountAt('/system/user')
    await screen.findByText('USER PAGE')
    const bc = document.querySelector('.shell-header .ant-breadcrumb') as HTMLElement
    expect(bc).toBeTruthy()
    expect(within(bc).getByText('系统')).toBeTruthy()
    expect(within(bc).getByText('用户')).toBeTruthy()
  })

  // 回归:header-brand 模式(horizontal 等)下,品牌与面包屑是两段独立兄弟块,面包屑不能被品牌抢占。
  // 旧的三路互斥三元会让这些模式里 showBreadcrumb 成死控件 —— 此断言在那种写法下必红。
  it('header-brand 模式(horizontal)开 showBreadcrumb:品牌与面包屑并存', async () => {
    useAppStore.setState({ showBreadcrumb: true, layoutMode: 'horizontal' })
    mountAt('/system/user')
    await screen.findByText('USER PAGE')
    expect(document.querySelector('.shell-header .shell-brand')).toBeTruthy() // 品牌在
    const bc = document.querySelector('.shell-header .ant-breadcrumb') as HTMLElement
    expect(bc).toBeTruthy() // 面包屑也在(并存,非互斥)
    expect(within(bc).getByText('系统')).toBeTruthy()
    expect(within(bc).getByText('用户')).toBeTruthy()
  })
})

describe('LayoutShell 顶栏', () => {
  it('点折叠按钮 → 翻转 app.collapsed', async () => {
    mountAt('/dashboard')
    await screen.findByText('DASH PAGE')
    expect(useAppStore.getState().collapsed).toBe(false)
    fireEvent.click(screen.getByLabelText('折叠菜单'))
    expect(useAppStore.getState().collapsed).toBe(true)
  })

  it('点主题按钮 → 翻明暗', async () => {
    mountAt('/dashboard')
    await screen.findByText('DASH PAGE')
    fireEvent.click(screen.getByLabelText('主题'))
    expect(useAppStore.getState().themeScheme).toBe('dark')
  })

  it('点登出 → 调后端登出 + 清会话 + 跳登录(后端失败也照跳)', async () => {
    logoutMock.mockRejectedValue(new Error('500'))
    mountAt('/dashboard')
    await screen.findByText('DASH PAGE')
    // 登出已并入用户下拉:点头像区(用户名 n)展开 → 点「退出登录」项
    fireEvent.click(screen.getByText('n'))
    fireEvent.click(await screen.findByText('退出登录'))
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/login', { replace: true }))
    expect(useUserStore.getState().accessToken).toBe('')
  })
})
