import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { type ReactElement, type ReactNode } from 'react'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import type { ProColumns } from '@ant-design/pro-components'
import '@/locales'
import { configApi } from '@/api'
import { useAuthStore } from '@/stores/auth'
import type { SysConfig } from '@/types/api'

const { confirmMock, reloadMock } = vi.hoisted(() => ({ confirmMock: vi.fn(), reloadMock: vi.fn() }))
vi.mock('@/hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: confirmMock, run: vi.fn(), ask: vi.fn() }) }))

// mock DataTable(撞 pro-components 墙):捕 columns/toolbar/fetcher,句柄暴露 reloadMock。
let dt: { columns?: ProColumns<SysConfig>[]; toolbar?: ReactNode; fetcher?: (q: Record<string, unknown>) => unknown } = {}
vi.mock('@/components/DataTable', async () => {
  const { forwardRef: fr, useImperativeHandle: ui } = await import('react')
  return { DataTable: fr(function MockDT(props: typeof dt, ref: React.Ref<{ reload: () => void }>) { dt = props; ui(ref, () => ({ reload: reloadMock })); return <div>{props.toolbar}</div> }) }
})

import OtherConfig from './OtherConfig'

const ROWS: SysConfig[] = [{ id: 1, configKey: 'biz.flag', configValue: 'on', name: '业务开关', groupCode: 'biz', sort: 0 }]
const mount = () => render(<AntdApp><OtherConfig /></AntdApp>)
type AnyEl = ReactElement<Record<string, any>>
const col = (key: string) => dt.columns!.find((c) => c.dataIndex === key || c.key === key)!
const callRender = (c: ProColumns<SysConfig>, r: SysConfig): AnyEl => (c.render as (d: unknown, e: SysConfig) => AnyEl)(null, r)

beforeEach(() => {
  dt = {}
  confirmMock.mockReset(); reloadMock.mockClear()
  confirmMock.mockImplementation(async (o: { action: () => Promise<unknown> }) => { await o.action(); return true })
  vi.spyOn(configApi, 'page').mockResolvedValue({ items: ROWS, total: 1 })
  vi.spyOn(configApi, 'add').mockResolvedValue(88)
  vi.spyOn(configApi, 'update').mockResolvedValue(true)
  vi.spyOn(configApi, 'remove').mockResolvedValue(true)
  useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
  vi.restoreAllMocks()
})

describe('OtherConfig 接线', () => {
  it('fetcher:排除结构化分组(sys/security/upload)', async () => {
    mount()
    await waitFor(() => expect(dt.fetcher).toBeTruthy())
    dt.fetcher!({ page: 1, pageSize: 10 })
    expect(configApi.page).toHaveBeenCalledWith(expect.objectContaining({ excludedGroupCodes: ['sys', 'security', 'upload', 'externalauth', 'job'] }))
  })

  it('新增:提交全部 6 字段(未填项落空白默认,无 C9 漏字段)', async () => {
    mount()
    await waitFor(() => expect(dt.toolbar).toBeTruthy())
    fireEvent.click(screen.getByRole('button', { name: /新\s*增/ }))
    fireEvent.change(await screen.findByPlaceholderText('配置键'), { target: { value: 'biz.x' } })
    fireEvent.change(screen.getByPlaceholderText('配置名称'), { target: { value: '开关' } })
    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))
    await waitFor(() => expect(configApi.add).toHaveBeenCalledWith({ configKey: 'biz.x', name: '开关', configValue: '', groupCode: '', sort: 0, remark: '' }))
  })

  it('编辑:提交全部 6 字段(禁用的 configKey 仍随 validateFields 返回)', async () => {
    mount()
    await waitFor(() => expect(dt.columns).toBeTruthy())
    const edit = callRender(col('op'), ROWS[0]).props.children[0] as AnyEl // 编辑按钮
    edit.props.onClick() // openEdit(ROWS[0]) → 表单回填 configToInput
    fireEvent.change(await screen.findByDisplayValue('业务开关'), { target: { value: '业务开关改' } })
    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))
    await waitFor(() => expect(configApi.update).toHaveBeenCalledWith(1, { configKey: 'biz.flag', configValue: 'on', name: '业务开关改', groupCode: 'biz', sort: 0, remark: '' }))
  })

  it('删除:confirm → remove → reload', async () => {
    mount()
    await waitFor(() => expect(dt.columns).toBeTruthy())
    const del = callRender(col('op'), ROWS[0]).props.children[1] as AnyEl
    del.props.onClick()
    await waitFor(() => expect(configApi.remove).toHaveBeenCalledWith(1))
    await waitFor(() => expect(reloadMock).toHaveBeenCalled())
  })

  it('操作列:无写/删权限 → 全空', async () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await waitFor(() => expect(dt.columns).toBeTruthy())
    const cell = callRender(col('op'), ROWS[0])
    expect(cell.props.children[0]).toBeFalsy() // 编辑
    expect(cell.props.children[1]).toBeFalsy() // 删除
  })
})
