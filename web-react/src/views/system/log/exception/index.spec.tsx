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

import ExceptionLogPage from './index'

const mount = () => render(<AntdApp><ExceptionLogPage /></AntdApp>)
const callRender = (c: ProColumns<Record<string, unknown>>, r: Record<string, unknown>) =>
  (c.render as (d: unknown, e: Record<string, unknown>) => ReactNode)(null, r)

beforeEach(() => {
  captured = {}
  reloadSpy.mockReset()
  confirmMock.mockReset()
  confirmMock.mockImplementation(async (o: { action: () => Promise<unknown> }) => { await o.action(); return true })
  vi.spyOn(logApi, 'exceptionPage').mockResolvedValue({ items: [], total: 0 })
  vi.spyOn(logApi, 'exceptionClear').mockResolvedValue(true)
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
  vi.restoreAllMocks()
})

describe('ExceptionLogPage 接线', () => {
  it('fetcher:异常类型/路径/时间范围映射到 exceptionPage', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await captured.fetcher!({ page: 1, pageSize: 10, exceptionType: 'NullRef', path: '/api/x', createTime: ['2026-07-01', '2026-07-02'] })
    expect(logApi.exceptionPage).toHaveBeenCalledWith({ page: 1, pageSize: 10, exceptionType: 'NullRef', path: '/api/x', createTime: ['2026-07-01', '2026-07-02'] })
  })

  it('fetcher:缺省搜索值收敛为 undefined / createTime null', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await captured.fetcher!({ page: 1, pageSize: 10 })
    expect(logApi.exceptionPage).toHaveBeenCalledWith({ page: 1, pageSize: 10, exceptionType: undefined, path: undefined, createTime: null })
  })

  it('详情抽屉:点详情 → 展示异常类型与堆栈', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    const opCol = captured.columns!.find((c) => c.key === 'op')!
    const row = { id: 1, httpMethod: 'GET', path: '/x', traceId: 'T1', exceptionType: 'System.NullReferenceException', message: 'boom', stackTrace: 'at Foo.Bar()', operatorId: 9, operatorName: '张三', ip: '1.2.3.4', userAgent: 'UA', createTime: '2026-07-01 10:00:00' }
    render(<AntdApp>{callRender(opCol, row)}</AntdApp>)
    fireEvent.click(screen.getByRole('button', { name: /详\s*情/ }))
    expect(await screen.findByText('System.NullReferenceException')).toBeTruthy()
    expect(screen.getByText('at Foo.Bar()')).toBeTruthy()
  })

  it('清空:DELETE 权限门 + 二确 → exceptionClear + reload', async () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['DELETE:/api/v1/sys/log/exception'] })
    mount()
    render(<AntdApp>{captured.toolbar}</AntdApp>)
    fireEvent.click(screen.getByRole('button', { name: /清\s*空\s*日\s*志/ }))
    await vi.waitFor(() => expect(logApi.exceptionClear).toHaveBeenCalled())
    await vi.waitFor(() => expect(reloadSpy).toHaveBeenCalled())
  })
})
