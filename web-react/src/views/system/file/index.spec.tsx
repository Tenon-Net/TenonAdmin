import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { fileApi } from '@/api'
import { useAuthStore } from '@/stores/auth'

// <DataTable> mock(同前:pro-components vitest 解析墙)。只测页面接线:fetcher 映射 + 工具栏权限门 + rowSelection 受控。
let captured: {
  fetcher?: (q: Record<string, unknown>) => Promise<unknown>
  toolbar?: React.ReactNode
  rowSelection?: { selectedRowKeys: React.Key[]; onChange: (k: React.Key[]) => void }
} = {}
vi.mock('@/components/DataTable', () => ({
  DataTable: (props: typeof captured) => {
    captured = props
    return <div data-testid="toolbar">{props.toolbar}</div>
  },
}))

import FilePage from './index'

function mount() {
  render(
    <AntdApp>
      <FilePage />
    </AntdApp>,
  )
}

beforeEach(() => {
  captured = {}
  vi.spyOn(fileApi, 'page').mockResolvedValue({ items: [], total: 0 })
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
})

describe('FilePage 接线', () => {
  it('fetcher:ProTable 的 {page,pageSize,originalName} → fileApi.page 强类型入参', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    expect(captured.fetcher).toBeTypeOf('function')
    await captured.fetcher!({ page: 2, pageSize: 20, originalName: 'doc' })
    expect(fileApi.page).toHaveBeenCalledWith({ page: 2, pageSize: 20, originalName: 'doc' })
  })

  it('fetcher:非字符串搜索键过滤成 undefined', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await captured.fetcher!({ page: 1, pageSize: 10, originalName: 42 })
    expect(fileApi.page).toHaveBeenCalledWith(expect.objectContaining({ originalName: undefined }))
  })

  it('工具栏「上传文件」按 POST 权限码门控:有码渲染 / 无码隐藏', () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['POST:/api/v1/sys/file/upload'] })
    mount()
    expect(screen.getByRole('button', { name: /上\s*传\s*文\s*件/ })).toBeTruthy()
    cleanup()
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['GET:/api/v1/sys/file/page'] })
    mount()
    expect(screen.queryByRole('button', { name: /上\s*传\s*文\s*件/ })).toBeNull()
  })

  it('工具栏「批量删除」按 POST 批删权限码门控:有码渲染 / 无码隐藏', () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['POST:/api/v1/sys/file/batch-delete'] })
    mount()
    expect(screen.getByRole('button', { name: /批\s*量\s*删\s*除/ })).toBeTruthy()
    cleanup()
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['POST:/api/v1/sys/file/upload'] })
    mount()
    expect(screen.queryByRole('button', { name: /批\s*量\s*删\s*除/ })).toBeNull()
  })

  it('rowSelection 受控:初始空选 + onChange 可写(批删勾选态由 useBatchDelete 托管)', () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    expect(captured.rowSelection).toBeTruthy()
    expect(captured.rowSelection!.selectedRowKeys).toEqual([])
    expect(captured.rowSelection!.onChange).toBeTypeOf('function')
  })
})
