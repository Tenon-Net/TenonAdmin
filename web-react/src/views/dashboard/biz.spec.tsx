import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, waitFor, fireEvent } from '@testing-library/react'
import '@/locales'
import { noticeApi } from '@/api'
import { useAuthStore } from '@/stores/auth'
import { MenuType, type MenuNode } from '@/types/menu'

const { navigateMock } = vi.hoisted(() => ({ navigateMock: vi.fn() }))
vi.mock('react-router-dom', () => ({
  useNavigate: () => navigateMock,
  useLocation: () => ({ pathname: '/business/workbench' }), // 当前页 = 工作台自身
}))

import Biz from './biz'

const leaf = (id: number, path: string, title: string, visible = true): MenuNode => ({
  id, parentId: 0, type: MenuType.Menu, title, path, sort: 0, visible, children: [],
})

beforeEach(() => {
  navigateMock.mockReset()
  vi.spyOn(noticeApi, 'mine').mockResolvedValue({
    items: [{ id: 1, title: '系统升级', type: 1, publishTime: '2026-07-21T08:00:00', isRead: false }],
    total: 1,
  } as any)
  // 第二个是当前页自身,应从快捷入口排除
  useAuthStore.setState({ menuTree: [leaf(1, '/a', '用户管理'), leaf(2, '/business/workbench', '工作台')] })
})
afterEach(() => { cleanup(); useAuthStore.setState({ menuTree: [] }); vi.restoreAllMocks() })

describe('Biz', () => {
  it('快捷入口 = 可见叶子且排除当前页自身', () => {
    render(<Biz />)
    expect(screen.getByText('用户管理')).toBeTruthy()
    expect(screen.queryByText('工作台')).toBeFalsy() // 自身被排除
  })
  it('点快捷入口 → 导航到其 path', () => {
    render(<Biz />)
    fireEvent.click(screen.getByText('用户管理'))
    expect(navigateMock).toHaveBeenCalledWith('/a')
  })
  it('我的通知渲染 + 查看全部跳 /personal/notice', async () => {
    render(<Biz />)
    await waitFor(() => expect(screen.getByText('系统升级')).toBeTruthy())
    fireEvent.click(screen.getByText('查看全部'))
    expect(navigateMock).toHaveBeenCalledWith('/personal/notice')
  })
})
