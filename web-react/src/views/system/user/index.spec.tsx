import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales' // t() 要真文案
import { userApi, roleApi } from '@/api'
import { useAuthStore } from '@/stores/auth'

/**
 * **`<DataTable>` 被 mock 掉**,原因同 DataTable.spec:真 DataTable 静态导入 ProTable,
 * 而 `@ant-design/pro-components` 在 vitest 的 externalize 下解析不了(无扩展名 deep import)。
 * 页面只依赖 `<DataTable>` 的 prop 契约,mock 它即可测**页面自己的接线** —— fetcher 的搜索/排序映射、
 * 工具栏按钮的权限门。真 ProTable 的列表/列持久化留 dev 实点 + E2 冒烟(B11 的开放问题)。
 */
let captured: { fetcher?: (q: Record<string, unknown>) => Promise<unknown>; toolbar?: React.ReactNode } = {}
vi.mock('@/components/DataTable', () => ({
  DataTable: (props: { fetcher?: (q: Record<string, unknown>) => Promise<unknown>; toolbar?: React.ReactNode }) => {
    captured = props
    return <div data-testid="toolbar">{props.toolbar}</div>
  },
}))

import UserPage from './index'

function mount() {
  render(
    <AntdApp>
      <UserPage />
    </AntdApp>,
  )
}

beforeEach(() => {
  captured = {}
  // 角色下拉在 mount 时拉取:桩掉,免未处理 rejection 噪音。
  vi.spyOn(roleApi, 'page').mockResolvedValue({ items: [], total: 0 })
  vi.spyOn(userApi, 'page').mockResolvedValue({ items: [], total: 0 })
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
})

describe('UserPage 接线', () => {
  it('fetcher:ProTable 的 {page,pageSize,account,sortField,sortOrder} → userApi.page 强类型入参', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    expect(captured.fetcher).toBeTypeOf('function')
    await captured.fetcher!({ page: 2, pageSize: 20, account: 'bob', sortField: 'account', sortOrder: 'desc' })
    expect(userApi.page).toHaveBeenCalledWith({
      page: 2, pageSize: 20, account: 'bob', name: undefined, sortField: 'account', sortOrder: 'desc',
    })
  })

  it('fetcher:非字符串的搜索值被过滤成 undefined(不是原样透传成脏参)', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await captured.fetcher!({ page: 1, pageSize: 10, account: 123, name: 'kate' })
    expect(userApi.page).toHaveBeenCalledWith(
      expect.objectContaining({ account: undefined, name: 'kate' }),
    )
  })

  it('工具栏「新增」按 POST 权限码门控:有码渲染', () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['POST:/api/v1/sys/user'] })
    mount()
    // antd 两个中文字按钮自动插空格:common.add='新增' → 可及名可能是「新 增」
    expect(screen.getByRole('button', { name: /新\s*增/ })).toBeTruthy()
  })

  it('工具栏「新增」按 POST 权限码门控:无码隐藏', () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['GET:/api/v1/sys/user/page'] })
    mount()
    expect(screen.queryByRole('button', { name: /新\s*增/ })).toBeNull()
  })
})
