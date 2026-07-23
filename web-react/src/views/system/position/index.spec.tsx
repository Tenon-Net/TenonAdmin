import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales' // t() 要真文案
import { positionApi } from '@/api'
import { useAuthStore } from '@/stores/auth'

// <DataTable> 被 mock(同 DataTable.spec:真 DataTable 静态导入 ProTable,vitest externalize 下解析不了)。
// 只测页面自己的接线:fetcher 的搜索/排序映射、工具栏按钮的权限门。真 ProTable 留 dev 实点 + E2。
let captured: { fetcher?: (q: Record<string, unknown>) => Promise<unknown>; toolbar?: React.ReactNode } = {}
vi.mock('@/components/DataTable', () => ({
  DataTable: (props: { fetcher?: (q: Record<string, unknown>) => Promise<unknown>; toolbar?: React.ReactNode }) => {
    captured = props
    return <div data-testid="toolbar">{props.toolbar}</div>
  },
}))

import PositionPage from './index'

function mount() {
  render(
    <AntdApp>
      <PositionPage />
    </AntdApp>,
  )
}

beforeEach(() => {
  captured = {}
  vi.spyOn(positionApi, 'page').mockResolvedValue({ items: [], total: 0 })
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
})

describe('PositionPage 接线', () => {
  it('fetcher:ProTable 的 {page,pageSize,name,sortField,sortOrder} → positionApi.page 强类型入参', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    expect(captured.fetcher).toBeTypeOf('function')
    await captured.fetcher!({ page: 2, pageSize: 20, name: 'dev', sortField: 'sort', sortOrder: 'asc' })
    expect(positionApi.page).toHaveBeenCalledWith({
      page: 2, pageSize: 20, name: 'dev', sortField: 'sort', sortOrder: 'asc',
    })
  })

  it('fetcher:非字符串 name 过滤成 undefined(不是原样透传成脏参)', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await captured.fetcher!({ page: 1, pageSize: 10, name: 123 })
    expect(positionApi.page).toHaveBeenCalledWith(expect.objectContaining({ name: undefined }))
  })

  it('工具栏「新增」按 POST 权限码门控:有码渲染', () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['POST:/api/v1/sys/position/add'] })
    mount()
    expect(screen.getByRole('button', { name: /新\s*增/ })).toBeTruthy()
  })

  it('工具栏「新增」按 POST 权限码门控:无码隐藏', () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['GET:/api/v1/sys/position/page'] })
    mount()
    expect(screen.queryByRole('button', { name: /新\s*增/ })).toBeNull()
  })
})
