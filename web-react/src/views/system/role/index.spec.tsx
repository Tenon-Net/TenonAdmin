import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { act, forwardRef, useImperativeHandle, type ReactElement, type ReactNode } from 'react'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import type { ProColumns } from '@ant-design/pro-components'
import '@/locales'
import { menuApi, moduleApi, roleApi, userApi } from '@/api'
import { useAuthStore } from '@/stores/auth'
import { DataScopeType, type SysRole } from '@/types/api'

const { confirmMock, gmtOnChange, upOpen, upConfirmRef, reloadMock } = vi.hoisted(() => ({ confirmMock: vi.fn(), gmtOnChange: { fn: null as null | ((ids: number[]) => void) }, upOpen: vi.fn(), upConfirmRef: { fn: null as null | ((ids: number[]) => void) }, reloadMock: vi.fn() }))
vi.mock('@/hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: confirmMock, run: vi.fn(), ask: vi.fn() }) }))

// mock DataTable(撞 pro-components 墙):捕 columns/toolbar/rowSelection。
let dt: { columns?: ProColumns<SysRole>[]; toolbar?: ReactNode } = {}
vi.mock('@/components/DataTable', async () => {
  const { forwardRef: fr, useImperativeHandle: ui } = await import('react')
  return { DataTable: fr(function MockDT(props: typeof dt, ref: React.Ref<{ reload: () => void }>) { dt = props; ui(ref, () => ({ reload: reloadMock })); return <div>{props.toolbar}</div> }) }
})
// mock 重子件:GrantMenuTable(捕 onCheckedChange 供触发 saveMenus)、UserPicker(捕 open/onConfirm)、OrgTreeSelect。
vi.mock('./GrantMenuTable', () => ({ GrantMenuTable: (p: { onCheckedChange: (ids: number[]) => void }) => { gmtOnChange.fn = p.onCheckedChange; return <div data-testid="gmt" /> } }))
vi.mock('@/components/UserPicker', () => ({
  UserPicker: forwardRef(function MockUP(p: { onConfirm: (ids: number[]) => void }, ref: React.Ref<{ open: (ids?: number[]) => void }>) {
    upConfirmRef.fn = p.onConfirm; useImperativeHandle(ref, () => ({ open: upOpen })); return null
  }),
}))
vi.mock('@/components/OrgTreeSelect', () => ({ OrgTreeSelect: () => <div data-testid="org-tree-select" /> }))

import RolePage from './index'

const ROLES: SysRole[] = [
  { id: 1, code: 'admin', name: '管理员', sort: 0, enabled: true, remark: '内置' },
  { id: 2, code: 'guest', name: '访客', sort: 1, enabled: false, remark: null },
]
const mount = () => render(<AntdApp><RolePage /></AntdApp>)
type AnyEl = ReactElement<Record<string, any>>
const col = (key: string) => dt.columns!.find((c) => c.dataIndex === key || c.key === key)!
const callRender = (c: ProColumns<SysRole>, r: SysRole): AnyEl => (c.render as (d: unknown, e: SysRole) => AnyEl)(null, r)

beforeEach(() => {
  dt = {}; gmtOnChange.fn = null; upConfirmRef.fn = null
  confirmMock.mockReset(); upOpen.mockReset(); reloadMock.mockClear()
  confirmMock.mockImplementation(async (o: { action: () => Promise<unknown> }) => { await o.action(); return true })
  vi.spyOn(roleApi, 'update').mockResolvedValue(true)
  vi.spyOn(roleApi, 'add').mockResolvedValue(88)
  vi.spyOn(roleApi, 'remove').mockResolvedValue(true)
  vi.spyOn(roleApi, 'getMenus').mockResolvedValue([2, 4])
  vi.spyOn(roleApi, 'setMenus').mockResolvedValue(true)
  vi.spyOn(roleApi, 'getDataScope').mockResolvedValue({ id: 9, roleId: 1, scopeType: DataScopeType.Custom, customOrgIds: '3,7' })
  vi.spyOn(roleApi, 'setDataScope').mockResolvedValue(true)
  vi.spyOn(roleApi, 'getUsers').mockResolvedValue([11, 12])
  vi.spyOn(roleApi, 'setUsers').mockResolvedValue(true)
  vi.spyOn(menuApi, 'tree').mockResolvedValue([])
  vi.spyOn(moduleApi, 'list').mockResolvedValue([])
  vi.spyOn(userApi, 'page').mockResolvedValue({ items: [], total: 3 })
  useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [], currentModuleId: 10 })
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [], currentModuleId: null })
  vi.restoreAllMocks()
})

describe('RolePage 接线', () => {
  it('应用选项加载失败时显示错误', async () => {
    vi.mocked(moduleApi.list).mockRejectedValueOnce(new Error('module options failed'))
    mount()
    expect(await screen.findByText('module options failed')).toBeTruthy()
  })

  it('状态列:全量 update(全字段 + enabled)', async () => {
    mount()
    await waitFor(() => expect(dt.columns).toBeTruthy())
    await callRender(col('enabled'), ROLES[0]).props.request(false)
    expect(roleApi.update).toHaveBeenCalledWith(1, { code: 'admin', name: '管理员', sort: 0, enabled: false, remark: '内置', isDelegatable: false })
  })

  it('状态列:切换成功回调必须重拉表格(悲观开关回显真实态)', async () => {
    // 漏 onChange={reload} 时:sw.props.onChange 为 undefined → toBeTruthy 红;调用 undefined 亦红。
    mount()
    await waitFor(() => expect(dt.columns).toBeTruthy())
    const sw = callRender(col('enabled'), ROLES[1]) // 访客 enabled=false
    expect(sw.props.onChange).toBeTruthy()
    sw.props.onChange(true)
    expect(reloadMock).toHaveBeenCalled()
  })

  it('状态列:无 PUT 权限置灰', async () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await waitFor(() => expect(dt.columns).toBeTruthy())
    expect(callRender(col('enabled'), ROLES[0]).props.disabled).toBe(true)
  })

  it('操作列 更多▾:超管含 授权菜单/数据范围/授权用户', async () => {
    mount()
    await waitFor(() => expect(dt.columns).toBeTruthy())
    const cell = callRender(col('op'), ROLES[0])
    const dropdown = cell.props.children[2] as AnyEl
    expect(dropdown.props.menu.items.map((i: { key: string }) => i.key)).toEqual(['menus', 'scope', 'users'])
  })

  it('更多▾ 授权菜单 → 拉菜单树 + 已授菜单', async () => {
    mount()
    await waitFor(() => expect(dt.columns).toBeTruthy())
    const dropdown = (callRender(col('op'), ROLES[0]).props.children[2] as AnyEl)
    dropdown.props.menu.onClick({ key: 'menus' })
    await waitFor(() => expect(menuApi.tree).toHaveBeenCalled())
    expect(roleApi.getMenus).toHaveBeenCalledWith(1)
  })

  it('删除:先查持有人数(userApi.page roleId)→ confirm → remove', async () => {
    mount()
    await waitFor(() => expect(dt.columns).toBeTruthy())
    const del = callRender(col('op'), ROLES[1]).props.children[1] as AnyEl // 访客 id2
    del.props.onClick()
    await waitFor(() => expect(userApi.page).toHaveBeenCalledWith(expect.objectContaining({ roleId: 2 })))
    await waitFor(() => expect(roleApi.remove).toHaveBeenCalledWith(2))
  })

  it('操作列:无写/授权权限 → 全空', async () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await waitFor(() => expect(dt.columns).toBeTruthy())
    const cell = callRender(col('op'), ROLES[0])
    expect(cell.props.children[0]).toBeFalsy() // 编辑
    expect(cell.props.children[1]).toBeFalsy() // 删除
    expect(cell.props.children[2]).toBeFalsy() // 更多
  })

  it('新增:提交全部字段', async () => {
    mount()
    await waitFor(() => expect(dt.columns).toBeTruthy())
    fireEvent.click(screen.getByRole('button', { name: /新\s*增/ }))
    fireEvent.change(await screen.findByPlaceholderText('角色编码'), { target: { value: 'ops' } })
    fireEvent.change(screen.getByPlaceholderText('角色名称'), { target: { value: '运维' } })
    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))
    await waitFor(() => expect(roleApi.add).toHaveBeenCalledWith({ code: 'ops', name: '运维', sort: 0, enabled: true, remark: '', isDelegatable: false }))
  })

  it('授权菜单保存:全量替换(setMenus 收当前勾选)', async () => {
    mount()
    await waitFor(() => expect(dt.columns).toBeTruthy())
    const dropdown = (callRender(col('op'), ROLES[0]).props.children[2] as AnyEl)
    dropdown.props.menu.onClick({ key: 'menus' })
    await waitFor(() => expect(gmtOnChange.fn).toBeTruthy()) // 抽屉开、GrantMenuTable 挂载
    await act(async () => { gmtOnChange.fn!([1, 2, 3]) }) // 用户改了勾选(act 刷新 state)
    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))
    await waitFor(() => expect(roleApi.setMenus).toHaveBeenCalledWith(1, [1, 2, 3]))
  })

  it('授权用户:open 回显已关联,confirm → setUsers 全量替换', async () => {
    mount()
    await waitFor(() => expect(dt.columns).toBeTruthy())
    const dropdown = (callRender(col('op'), ROLES[0]).props.children[2] as AnyEl)
    dropdown.props.menu.onClick({ key: 'users' })
    await waitFor(() => expect(upOpen).toHaveBeenCalledWith([11, 12])) // 回显已关联用户
    await upConfirmRef.fn!([11, 20])
    expect(roleApi.setUsers).toHaveBeenCalledWith(1, [11, 20])
  })

  it('数据范围:自定义时回显 customOrgIds(逗号串→数组);保存带上', async () => {
    mount()
    await waitFor(() => expect(dt.columns).toBeTruthy())
    const dropdown = (callRender(col('op'), ROLES[0]).props.children[2] as AnyEl)
    dropdown.props.menu.onClick({ key: 'scope' })
    await waitFor(() => expect(roleApi.getDataScope).toHaveBeenCalledWith(1))
    // 抽屉开 → 保存 → setDataScope(自定义 → 带 customOrgIds=[3,7])
    await screen.findByText('数据范围')
    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))
    await waitFor(() => expect(roleApi.setDataScope).toHaveBeenCalledWith(1, DataScopeType.Custom, [3, 7]))
  })
})
