import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { forwardRef, useImperativeHandle, type ReactNode } from 'react'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import type { ProColumns } from '@ant-design/pro-components'
import '@/locales'
import { logApi } from '@/api'
import { useAuthStore } from '@/stores/auth'

const { reloadSpy, confirmMock } = vi.hoisted(() => ({ reloadSpy: vi.fn(), confirmMock: vi.fn() }))
vi.mock('@/hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: confirmMock, ask: vi.fn(), run: vi.fn() }) }))
let captured: { columns?: ProColumns<Record<string, unknown>>[]; fetcher?: (q: Record<string, unknown>) => Promise<unknown>; toolbar?: ReactNode } = {}
vi.mock('@/components/DataTable', () => ({
  DataTable: forwardRef((props: typeof captured, ref: React.Ref<{ reload: () => void }>) => {
    captured = props
    useImperativeHandle(ref, () => ({ reload: reloadSpy }))
    return <div data-testid="dt" />
  }),
}))

import LoginLogPage from './index'

const mount = () => render(<AntdApp><LoginLogPage /></AntdApp>)
const findCol = (key: string) => captured.columns!.find((c) => c.dataIndex === key)!
const callRender = (c: ProColumns<Record<string, unknown>>, r: Record<string, unknown>) =>
  (c.render as (d: unknown, e: Record<string, unknown>) => ReactNode)(null, r)

beforeEach(() => {
  captured = {}
  reloadSpy.mockReset()
  confirmMock.mockReset()
  confirmMock.mockImplementation(async (o: { action: () => Promise<unknown> }) => { await o.action(); return true })
  vi.spyOn(logApi, 'loginPage').mockResolvedValue({ items: [], total: 0 })
  vi.spyOn(logApi, 'loginClear').mockResolvedValue(true)
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
  vi.restoreAllMocks()
})

describe('LoginLogPage 接线', () => {
  it('fetcher:账号/成败/时间范围映射到 loginPage', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await captured.fetcher!({ page: 3, pageSize: 15, account: 'admin', success: true, createTime: ['2026-07-01', '2026-07-02'] })
    expect(logApi.loginPage).toHaveBeenCalledWith({ page: 3, pageSize: 15, account: 'admin', success: true, createTime: ['2026-07-01', '2026-07-02'] })
  })

  it('resultCode 列:命中码翻人话、未命中回落原始数字', () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    const col = findCol('resultCode')
    render(<AntdApp>{callRender(col, { resultCode: 40004 })}</AntdApp>)
    expect(screen.getByText('账号已锁定,请稍后再试')).toBeTruthy()
    cleanup()
    render(<AntdApp>{callRender(col, { resultCode: 99999 })}</AntdApp>)
    expect(screen.getByText('99999')).toBeTruthy()
  })

  it('姓名列:姓名缺失回落账号', () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    const col = findCol('name')
    render(<AntdApp>{callRender(col, { name: null, account: 'ghost' })}</AntdApp>)
    expect(screen.getByText('ghost')).toBeTruthy()
  })

  it('清空:DELETE 权限门 + 二确 → loginClear + reload', async () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['DELETE:/api/v1/sys/log/login'] })
    mount()
    render(<AntdApp>{captured.toolbar}</AntdApp>)
    fireEvent.click(screen.getByRole('button', { name: /清\s*空\s*日\s*志/ }))
    await vi.waitFor(() => expect(logApi.loginClear).toHaveBeenCalled())
    await vi.waitFor(() => expect(reloadSpy).toHaveBeenCalled())
  })
})
