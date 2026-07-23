import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { noticeApi, roleApi, userApi } from '@/api'
import { useAuthStore } from '@/stores/auth'

// <DataTable> mock(同前:pro-components vitest 解析墙)。只测页面接线:fetcher 映射 + 工具栏权限门。
let captured: { fetcher?: (q: Record<string, unknown>) => Promise<unknown>; toolbar?: React.ReactNode } = {}
vi.mock('@/components/DataTable', () => ({
  DataTable: (props: { fetcher?: (q: Record<string, unknown>) => Promise<unknown>; toolbar?: React.ReactNode }) => {
    captured = props
    return <div data-testid="toolbar">{props.toolbar}</div>
  },
}))

import NoticePage from './index'

function mount() {
  render(
    <AntdApp>
      <NoticePage />
    </AntdApp>,
  )
}

beforeEach(() => {
  captured = {}
  vi.spyOn(noticeApi, 'page').mockResolvedValue({ items: [], total: 0 })
  vi.spyOn(roleApi, 'page').mockResolvedValue({ items: [], total: 0 })
  vi.spyOn(userApi, 'page').mockResolvedValue({ items: [], total: 0 })
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
})

describe('NoticePage 接线', () => {
  it('fetcher:ProTable 的 {page,pageSize,title} → noticeApi.page 强类型入参', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    expect(captured.fetcher).toBeTypeOf('function')
    await captured.fetcher!({ page: 3, pageSize: 15, title: '维护' })
    expect(noticeApi.page).toHaveBeenCalledWith({ page: 3, pageSize: 15, title: '维护' })
  })

  it('fetcher:非字符串 title 过滤成 undefined', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await captured.fetcher!({ page: 1, pageSize: 10, title: 42 })
    expect(noticeApi.page).toHaveBeenCalledWith(expect.objectContaining({ title: undefined }))
  })

  it('工具栏「发布通知」按 POST 权限码门控:有码渲染', () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['POST:/api/v1/sys/notice'] })
    mount()
    expect(screen.getByRole('button', { name: /发\s*布\s*通\s*知/ })).toBeTruthy()
  })

  it('工具栏「发布通知」按 POST 权限码门控:无码隐藏', () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['GET:/api/v1/sys/notice/page'] })
    mount()
    expect(screen.queryByRole('button', { name: /发\s*布\s*通\s*知/ })).toBeNull()
  })
})
