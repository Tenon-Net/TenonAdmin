import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { forwardRef, useImperativeHandle, type ReactNode } from 'react'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import type { ProColumns } from '@ant-design/pro-components'
import '@/locales'
import { logApi } from '@/api'
import { UserSelect } from '@/components/UserSelect'
import { useAuthStore } from '@/stores/auth'

const { reloadSpy, confirmMock } = vi.hoisted(() => ({ reloadSpy: vi.fn(), confirmMock: vi.fn() }))
vi.mock('@/hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: confirmMock, ask: vi.fn(), run: vi.fn() }) }))
// mock DataTable:捕获 columns/fetcher/toolbar,forwardRef 暴露 reload spy(验清空后刷新)。
let captured: { columns?: ProColumns<Record<string, unknown>>[]; fetcher?: (q: Record<string, unknown>) => Promise<unknown>; toolbar?: ReactNode } = {}
vi.mock('@/components/DataTable', () => ({
  DataTable: forwardRef((props: typeof captured, ref: React.Ref<{ reload: () => void }>) => {
    captured = props
    useImperativeHandle(ref, () => ({ reload: reloadSpy }))
    return <div data-testid="dt" />
  }),
}))

import OpLogPage from './index'

const mount = () => render(<AntdApp><OpLogPage /></AntdApp>)
const callRender = (c: ProColumns<Record<string, unknown>>, r: Record<string, unknown>) =>
  (c.render as (d: unknown, e: Record<string, unknown>) => ReactNode)(null, r)

beforeEach(() => {
  captured = {}
  reloadSpy.mockReset()
  confirmMock.mockReset()
  confirmMock.mockImplementation(async (o: { action: () => Promise<unknown> }) => { await o.action(); return true })
  vi.spyOn(logApi, 'opPage').mockResolvedValue({ items: [], total: 0 })
  vi.spyOn(logApi, 'opClear').mockResolvedValue(true)
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
  vi.restoreAllMocks()
})

describe('OpLogPage 接线', () => {
  it('fetcher:搜索表单值逐字段映射到 opPage(含 createTime 元组)', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await captured.fetcher!({ page: 2, pageSize: 20, title: 'del', path: '/a', success: false, operatorId: 7, createTime: ['2026-07-01', '2026-07-02'] })
    expect(logApi.opPage).toHaveBeenCalledWith({ page: 2, pageSize: 20, title: 'del', path: '/a', success: false, operatorId: 7, createTime: ['2026-07-01', '2026-07-02'] })
  })

  it('fetcher:缺省/错类型搜索值收敛为 undefined / createTime null', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await captured.fetcher!({ page: 1, pageSize: 10 })
    expect(logApi.opPage).toHaveBeenCalledWith({ page: 1, pageSize: 10, title: undefined, path: undefined, success: undefined, operatorId: undefined, createTime: null })
  })

  it('清空:DELETE 权限门 + 二确 → opClear + reload', async () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['DELETE:/api/v1/sys/log/op'] })
    mount()
    render(<AntdApp>{captured.toolbar}</AntdApp>)
    fireEvent.click(screen.getByRole('button', { name: /清\s*空\s*日\s*志/ }))
    await vi.waitFor(() => expect(logApi.opClear).toHaveBeenCalled())
    await vi.waitFor(() => expect(reloadSpy).toHaveBeenCalled())
  })

  it('清空按钮无 DELETE 权限不渲染', () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: [] })
    mount()
    render(<AntdApp>{captured.toolbar}</AntdApp>)
    expect(screen.queryByRole('button', { name: /清\s*空/ })).toBeNull()
  })

  it('operatorId 搜索项:formItemRender 裸返 UserSelect,不显式写 value/onChange(否则盖掉 pro-components cloneElement 注入的绑定 → 筛选静默失效,C7 HIGH-1)', () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    const col = captured.columns!.find((c) => c.dataIndex === 'operatorId')!
    const el = (col.formItemRender as (...a: unknown[]) => React.ReactElement<{ value?: unknown; onChange?: unknown }>)({}, {}, {})
    expect(el.type).toBe(UserSelect)
    // 注入靠 cloneElement,`newDom.props` 最后展开会盖掉注入 —— 显式写任一个都会重新引入 HIGH-1。
    expect(el.props.onChange).toBeUndefined()
    expect(el.props.value).toBeUndefined()
  })

  it('详情抽屉:点详情 → 展示操作名与操作人', async () => {
    useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
    mount()
    const opCol = captured.columns!.find((c) => c.key === 'op')!
    const row = { id: 1, title: '删除用户', httpMethod: 'DELETE', path: '/x', resultCode: 0, success: true, elapsedMs: 12, operatorId: 9, operatorName: '张三', ip: '1.2.3.4', userAgent: 'UA', createTime: '2026-07-01 10:00:00', paramJson: '{"a":1}' }
    render(<AntdApp>{callRender(opCol, row)}</AntdApp>)
    fireEvent.click(screen.getByRole('button', { name: /详\s*情/ }))
    expect(await screen.findByText('删除用户')).toBeTruthy()
    expect(screen.getByText('张三')).toBeTruthy()
  })
})
