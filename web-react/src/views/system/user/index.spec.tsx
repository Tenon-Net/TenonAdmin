import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales' // t() 要真文案
import { userApi, roleApi, dictApi, orgApi } from '@/api'
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
  // mount 时的三处副作用取数全桩掉,免真发 openapi-fetch 请求打出 ECONNREFUSED 噪音
  // (被各自 .catch 吞掉、测试仍绿,但噪音会盖住真错,且将来 vitest 收紧未处理错误就会红)。
  vi.spyOn(roleApi, 'page').mockResolvedValue({ items: [], total: 0 })
  vi.spyOn(orgApi, 'list').mockResolvedValue([])
  vi.spyOn(userApi, 'page').mockResolvedValue({ items: [], total: 0 })
  vi.spyOn(dictApi, 'items').mockResolvedValue([]) // useDictOptions('gender')
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
})

describe('UserPage 接线', () => {
  it('关键筛选数据加载失败时显示错误而不是静默隐藏', async () => {
    vi.mocked(orgApi.list).mockRejectedValueOnce(new Error('org options failed'))
    vi.mocked(roleApi.page).mockRejectedValueOnce(new Error('role options failed'))
    mount()
    expect(await screen.findByText('org options failed')).toBeTruthy()
    expect(await screen.findByText('role options failed')).toBeTruthy()
  })

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
