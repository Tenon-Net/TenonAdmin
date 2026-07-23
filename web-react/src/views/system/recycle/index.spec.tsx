import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import type { ProColumns } from '@ant-design/pro-components'
import '@/locales'
import { recycleApi, type RecycleBinItem } from '@/api'
import { useAuthStore } from '@/stores/auth'

// mock useConfirm:自动执行 action 并返回 true(以观测 restore/purge 的入参)。
const { confirmMock } = vi.hoisted(() => ({ confirmMock: vi.fn() }))
vi.mock('@/hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: confirmMock }) }))

// mock DataTable:捕获 columns/fetcher。Tabs 是真 antd,切页签走真实交互。
let captured: {
  fetcher?: (q: Record<string, unknown>) => Promise<unknown>
  columns?: ProColumns<RecycleBinItem>[]
} = {}
vi.mock('@/components/DataTable', () => ({
  DataTable: (props: typeof captured) => {
    captured = props
    return <div data-testid="dt" />
  },
}))

import RecyclePage from './index'

function mount() {
  render(
    <AntdApp>
      <RecyclePage />
    </AntdApp>,
  )
}
const row = (o: Partial<RecycleBinItem>): RecycleBinItem => ({ id: 1, name: 'x', code: null, deletedAt: null, deletedBy: null, ...o })
const callRender = (c: ProColumns<RecycleBinItem>, r: RecycleBinItem) =>
  (c.render as (d: unknown, e: RecycleBinItem) => React.ReactNode)(null, r)

let innerPage: ReturnType<typeof vi.fn>
beforeEach(() => {
  captured = {}
  innerPage = vi.fn().mockResolvedValue({ items: [], total: 0 })
  vi.spyOn(recycleApi, 'page').mockReturnValue(innerPage as unknown as ReturnType<typeof recycleApi.page>)
  vi.spyOn(recycleApi, 'restore').mockResolvedValue(true)
  vi.spyOn(recycleApi, 'purge').mockResolvedValue(true)
  confirmMock.mockReset()
  confirmMock.mockImplementation(async (o: { action: () => Promise<unknown> }) => { await o.action(); return true })
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
})

describe('RecyclePage 接线', () => {
  it('fetcher:按当前页签类型柯里化(初始 user)', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await captured.fetcher!({ page: 1, pageSize: 10 })
    expect(recycleApi.page).toHaveBeenCalledWith('user')
    expect(innerPage).toHaveBeenCalledWith({ page: 1, pageSize: 10 })
  })

  it('切页签 → fetcher 改拉新类型', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    fireEvent.click(screen.getByRole('tab', { name: '角色' })) // → activeTab = 'role'
    await captured.fetcher!({ page: 1, pageSize: 10 })
    expect(recycleApi.page).toHaveBeenLastCalledWith('role')
  })

  // 在**非默认**页签(角色)上验证:钉死「把 restore/purge 的 activeTab 硬编码成 'user'」这类变异
  //(只在 user 页签断言的话,该硬编码会存活——这正是本页最高危的"选错类型静默还原错实体"接缝)。
  it('恢复 / 彻底删按 activeTab + 行 id 定位(切到角色页签)', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    fireEvent.click(screen.getByRole('tab', { name: '角色' })) // → activeTab = 'role',columns 随之重算
    const op = captured.columns!.find((c) => c.key === 'op')!
    render(<AntdApp>{callRender(op, row({ id: 5 }))}</AntdApp>)
    fireEvent.click(screen.getByRole('button', { name: /恢\s*复/ }))
    await Promise.resolve()
    expect(recycleApi.restore).toHaveBeenCalledWith('role', 5)
    fireEvent.click(screen.getByRole('button', { name: /彻\s*底\s*删\s*除/ }))
    await Promise.resolve()
    expect(recycleApi.purge).toHaveBeenCalledWith('role', 5)
  })

  it('权限门:无恢复码不渲染恢复钮 / 有彻底删码渲染彻底删钮', () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['DELETE:/api/v1/sys/recycle/{type}/{id}'] })
    mount()
    const op = captured.columns!.find((c) => c.key === 'op')!
    render(<AntdApp>{callRender(op, row({ id: 5 }))}</AntdApp>)
    expect(screen.queryByRole('button', { name: /恢\s*复/ })).toBeNull()
    expect(screen.getByRole('button', { name: /彻\s*底\s*删\s*除/ })).toBeTruthy()
  })
})
