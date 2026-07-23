import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import type { ProColumns } from '@ant-design/pro-components'
import '@/locales'
import { sessionApi } from '@/api'
import { useAuthStore } from '@/stores/auth'
import { useUserStore } from '@/stores/user'
import type { OnlineSessionItem } from '@/types/api'

// <DataTable> mock:捕获 columns/fetcher。只测页面接线:fetcher 映射 + 设备列 uaSummary + 操作列自踢置灰/权限门。
let captured: {
  fetcher?: (q: Record<string, unknown>) => Promise<unknown>
  columns?: ProColumns<OnlineSessionItem>[]
} = {}
vi.mock('@/components/DataTable', () => ({
  DataTable: (props: typeof captured) => {
    captured = props
    return <div data-testid="dt" />
  },
}))

import SessionPage from './index'

function mount() {
  render(
    <AntdApp>
      <SessionPage />
    </AntdApp>,
  )
}
const EDGE_WIN = 'Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/120 Safari/537.36 Edg/120'
const row = (o: Partial<OnlineSessionItem>): OnlineSessionItem =>
  ({ sessionId: 's', userId: 1, account: 'a', ip: null, userAgent: null, ...o })
// ProColumns render 签名 (dom, entity, index, action, schema);测试只用前两个。
const callRender = (c: ProColumns<OnlineSessionItem>, r: OnlineSessionItem) =>
  (c.render as (d: unknown, e: OnlineSessionItem) => React.ReactNode)(null, r)

beforeEach(() => {
  captured = {}
  vi.spyOn(sessionApi, 'online').mockResolvedValue({ items: [], total: 0 })
  useUserStore.setState({ userInfo: { userId: 7, account: 'me', name: 'Me', mustChangePassword: false } })
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
})

describe('SessionPage 接线', () => {
  it('fetcher:{page,pageSize} → sessionApi.online 强类型入参', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await captured.fetcher!({ page: 2, pageSize: 20 })
    expect(sessionApi.online).toHaveBeenCalledWith({ page: 2, pageSize: 20 })
  })

  it('设备列:UA → uaSummary(浏览器 · 系统)', () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    const device = captured.columns!.find((c) => c.dataIndex === 'device')!
    expect(callRender(device, row({ userAgent: EDGE_WIN }))).toBe('Edge · Windows 10/11')
    expect(callRender(device, row({ userAgent: null }))).toBe('—')
  })

  it('操作列:踢自己置灰(current)/踢他人可点', () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    const op = captured.columns!.find((c) => c.key === 'op')!
    render(<AntdApp>{callRender(op, row({ userId: 7, account: 'me' }))}</AntdApp>) // userId === current(7)
    expect(screen.getByRole('button', { name: /当\s*前\s*会\s*话/ })).toHaveProperty('disabled', true)
    cleanup()
    render(<AntdApp>{callRender(op, row({ userId: 9, account: 'other' }))}</AntdApp>)
    expect(screen.getByRole('button', { name: /强\s*制\s*下\s*线/ })).toHaveProperty('disabled', false)
  })

  it('操作列:无强退权限 → 不渲染按钮', () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['GET:/api/v1/sys/session/online'] })
    mount()
    const op = captured.columns!.find((c) => c.key === 'op')!
    expect(callRender(op, row({ userId: 9 }))).toBeNull()
  })
})
