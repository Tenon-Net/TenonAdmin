import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { type ReactElement, type ReactNode } from 'react'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import type { ProColumns } from '@ant-design/pro-components'
import '@/locales'
import { menuApi, moduleApi } from '@/api'
import { useAuthStore } from '@/stores/auth'
import { MenuType, type MenuTreeNode } from '@/types/menu'

const { confirmMock, enterMock } = vi.hoisted(() => ({ confirmMock: vi.fn(), enterMock: vi.fn() }))
vi.mock('@/hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: confirmMock, run: vi.fn(), ask: vi.fn() }) }))
vi.mock('@/composables/useModule', () => ({ enter: enterMock })) // syncShell 走它

// mock TreeTable(撞 pro-components 墙,只测接线)+ ButtonManager(捕 menu prop 验打开)+ IconPicker(表单子件)。
let captured: { columns?: ProColumns<MenuTreeNode>[]; data?: MenuTreeNode[]; expandedRowKeys?: number[]; toolbar?: ReactNode } = {}
vi.mock('@/components/TreeTable', () => ({
  TreeTable: (props: typeof captured) => { captured = props; return <div data-testid="tt">{props.toolbar}</div> },
}))
let bmProps: { menu?: { id: number; title: string } | null } = {}
vi.mock('./ButtonManager', () => ({ ButtonManager: (p: typeof bmProps) => { bmProps = p; return null } }))
vi.mock('@/components/IconPicker', () => ({ IconPicker: () => <div data-testid="icon-picker" /> }))

import MenuPage from './index'

const btn = (id: number, parentId: number, permission: string): MenuTreeNode =>
  ({ id, parentId, type: MenuType.Button, title: `b${id}`, permission, sort: 0, enabled: true, moduleId: null, path: null, component: null, icon: null, visible: true, children: [] })
const TREE: MenuTreeNode[] = [
  { id: 1, parentId: 0, type: MenuType.Catalog, title: '系统', permission: '', sort: 0, enabled: true, moduleId: 10, path: null, component: null, icon: 'ph:gear', visible: true, children: [
    { id: 2, parentId: 1, type: MenuType.Menu, title: '用户', permission: '', sort: 0, enabled: true, moduleId: null, path: '/sys/user', component: 'system/user/index', icon: null, visible: true, children: [btn(4, 2, 'GET:/api/v1/sys/user'), btn(5, 2, 'POST:/api/v1/sys/user/add')] },
    { id: 3, parentId: 1, type: MenuType.Menu, title: '角色', permission: '', sort: 1, enabled: false, moduleId: null, path: '/sys/role', component: 'system/role/index', icon: null, visible: false, children: [] },
  ] },
  { id: 6, parentId: 0, type: MenuType.Catalog, title: '未分配', permission: '', sort: 9, enabled: true, moduleId: null, path: null, component: null, icon: null, visible: true, children: [] },
]

const mount = () => render(<AntdApp><MenuPage /></AntdApp>)
type AnyEl = ReactElement<Record<string, any>>
const col = (key: string) => captured.columns!.find((c) => c.dataIndex === key || c.key === key)!
const callRender = (c: ProColumns<MenuTreeNode>, r: MenuTreeNode): AnyEl => (c.render as (d: unknown, e: MenuTreeNode) => AnyEl)(null, r)

beforeEach(() => {
  captured = {}; bmProps = {}
  confirmMock.mockReset(); enterMock.mockReset()
  confirmMock.mockImplementation(async (o: { action: () => Promise<unknown> }) => { await o.action(); return true })
  enterMock.mockResolvedValue({ chooser: false, moduleId: 10 })
  vi.spyOn(menuApi, 'tree').mockResolvedValue(TREE)
  vi.spyOn(menuApi, 'routes').mockResolvedValue([])
  vi.spyOn(menuApi, 'update').mockResolvedValue(true)
  vi.spyOn(menuApi, 'remove').mockResolvedValue(true)
  vi.spyOn(menuApi, 'add').mockResolvedValue(88)
  vi.spyOn(moduleApi, 'list').mockResolvedValue([{ id: 10, code: 'sys', title: '系统应用', apiPrefix: 'sys', sort: 0, enabled: true }])
  // 默认应用 10 → moduleFilter 起始跟随;超管放行全部权限门。
  useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [], currentModuleId: 10 })
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [], currentModuleId: null })
  vi.restoreAllMocks()
})

describe('MenuPage 接线', () => {
  it('load + 按应用过滤(仅顶级)+ 剥按钮 + 播种展开', async () => {
    mount()
    await waitFor(() => expect(captured.data?.length).toBe(1)) // moduleId 10 只有「系统」这一顶级
    const sys = captured.data![0]
    expect(sys.id).toBe(1)
    expect(sys.children.map((c) => c.id)).toEqual([2, 3]) // 用户/角色
    expect(sys.children[0].children).toEqual([]) // 用户页的两个按钮被剥
    await waitFor(() => expect(captured.expandedRowKeys).toEqual([1])) // 只有系统有子 → 播种展开它
  })

  it('关键字搜到按钮权限码(按钮已剥出树,靠父页 buttonInfo 命中)', async () => {
    mount()
    await waitFor(() => expect(captured.data?.length).toBe(1))
    fireEvent.change(screen.getByPlaceholderText('搜索名称/路由/权限码'), { target: { value: 'user/add' } })
    await waitFor(() => {
      const sys = captured.data![0]
      expect(sys.children.map((c) => c.id)).toEqual([2]) // 只剩「用户」(它的按钮码命中);角色被过滤
    })
  })

  it('StatusSwitch:全量 update(全字段 + enabled),停用才确认', async () => {
    mount()
    await waitFor(() => expect(captured.columns).toBeTruthy())
    const el = callRender(col('enabled'), TREE[0].children[0]) // 用户 id2
    await el.props.request(false)
    expect(menuApi.update).toHaveBeenCalledWith(2, { parentId: 1, type: MenuType.Menu, title: '用户', permission: '', sort: 0, enabled: false, moduleId: null, path: '/sys/user', component: 'system/user/index', icon: '', visible: true })
    expect(el.props.confirm(true)).toBeNull() // 启用跳过确认
    expect(el.props.confirm(false)).toContain('用户') // 停用出确认文案
  })

  it('StatusSwitch:无 PUT 权限置灰', async () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await waitFor(() => expect(captured.columns).toBeTruthy())
    expect(callRender(col('enabled'), TREE[0].children[0]).props.disabled).toBe(true)
  })

  it('权限按钮列:页面显示入口、点击打开 ButtonManager;目录无按钮则不显示', async () => {
    mount()
    await waitFor(() => expect(captured.columns).toBeTruthy())
    expect(callRender(col('buttons'), TREE[0])).toBeNull() // 目录(系统)无直属按钮 → 不显示
    const cell = callRender(col('buttons'), TREE[0].children[0]) // 用户页(2 按钮)
    cell.props.onClick()
    await waitFor(() => expect(bmProps.menu).toEqual({ id: 2, title: '用户' }))
  })

  it('操作列 更多▾:删除走 confirm → remove + 重拉 + syncShell', async () => {
    mount()
    await waitFor(() => expect(captured.columns).toBeTruthy())
    const cell = callRender(col('op'), TREE[0].children[1]) // 角色 id3
    const dropdown = cell.props.children[1] as AnyEl
    expect(dropdown.props.menu.items.map((i: { key: string }) => i.key)).toEqual(['addChild', 'delete'])
    dropdown.props.menu.onClick({ key: 'delete' })
    await waitFor(() => expect(menuApi.remove).toHaveBeenCalledWith(3))
    await waitFor(() => expect(vi.mocked(menuApi.tree).mock.calls.length).toBeGreaterThan(1)) // 重拉
    await waitFor(() => expect(enterMock).toHaveBeenCalledWith(10)) // syncShell 重建当前应用壳层
  })

  it('无写权限:操作列无编辑无更多、权限按钮列不显示', async () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await waitFor(() => expect(captured.columns).toBeTruthy())
    const cell = callRender(col('op'), TREE[0].children[0])
    expect(cell.props.children[0]).toBeFalsy() // 编辑钮
    expect(cell.props.children[1]).toBeFalsy() // 更多下拉
    expect(callRender(col('buttons'), TREE[0].children[0])).toBeNull() // 无 add 权限 → 无配置权限入口
  })
})
