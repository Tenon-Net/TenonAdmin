import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { menuApi } from '@/api'
import { useAuthStore } from '@/stores/auth'
import { MenuType, type MenuTreeNode } from '@/types/menu'
import type { PermissionRouteItem } from '@/types/api'
import { ButtonManager } from './ButtonManager'

// ButtonManager 用原生 antd Table(非 ProTable),故可真渲染。仅 mock useConfirm(删除确认自动放行)。
const { confirmMock } = vi.hoisted(() => ({ confirmMock: vi.fn() }))
vi.mock('@/hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: confirmMock, run: vi.fn(), ask: vi.fn() }) }))

const btn = (id: number, permission: string, title: string): MenuTreeNode =>
  ({ id, parentId: 2, type: MenuType.Button, title, permission, sort: 0, enabled: true, moduleId: null, path: null, component: null, icon: null, visible: true, children: [] })
// 菜单 2「用户」下挂 b4/b5 两个按钮。
const TREE: MenuTreeNode[] = [
  { id: 2, parentId: 1, type: MenuType.Menu, title: '用户', permission: '', sort: 0, enabled: true, moduleId: null, path: '/sys/user', component: 'system/user/index', icon: null, visible: true, children: [
    btn(4, 'GET:/api/v1/sys/user', '查询'),
    btn(5, 'POST:/api/v1/sys/user/add', '新增'),
  ] },
]
const ROUTES: PermissionRouteItem[] = [
  { code: 'GET:/api/v1/sys/user', method: 'GET', path: '/api/v1/sys/user' },       // b4 已用 → 批量排除
  { code: 'DELETE:/api/v1/sys/user/1', method: 'DELETE', path: '/api/v1/sys/user/1' }, // 未用 → 批量可选,默认标题 删除
  { code: 'POST:/api/v1/biz/order', method: 'POST', path: '/api/v1/biz/order' },    // biz 未用
]

const onChanged = vi.fn()
const onClose = vi.fn()
const renderBM = (appPrefix?: string) =>
  render(<AntdApp><ButtonManager menu={{ id: 2, title: '用户' }} tree={TREE} routes={ROUTES} appPrefix={appPrefix} onClose={onClose} onChanged={onChanged} /></AntdApp>)

beforeEach(() => {
  confirmMock.mockReset()
  confirmMock.mockImplementation(async (o: { action: () => Promise<unknown> }) => { await o.action(); return true })
  onChanged.mockReset(); onClose.mockReset()
  vi.spyOn(menuApi, 'add').mockResolvedValue(88)
  vi.spyOn(menuApi, 'update').mockResolvedValue(true)
  vi.spyOn(menuApi, 'remove').mockResolvedValue(true)
  useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
  vi.restoreAllMocks()
})

describe('ButtonManager', () => {
  it('列表弹窗渲染当前菜单的按钮(权限码)', async () => {
    renderBM()
    expect(await screen.findByText('GET:/api/v1/sys/user')).toBeTruthy()
    expect(screen.getByText('POST:/api/v1/sys/user/add')).toBeTruthy()
  })

  it('无新增权限:不渲染 新增按钮 / 批量入口', async () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: [] })
    renderBM()
    await screen.findByText('GET:/api/v1/sys/user')
    expect(screen.queryByText('新增按钮')).toBeNull()
    expect(screen.queryByText('从路由批量添加')).toBeNull()
  })

  it('删除按钮:confirm → menuApi.remove(id) + onChanged', async () => {
    renderBM()
    await screen.findByText('GET:/api/v1/sys/user')
    fireEvent.click(screen.getAllByText('删除')[0]) // b4 行的删除
    await waitFor(() => expect(menuApi.remove).toHaveBeenCalledWith(4))
    expect(onChanged).toHaveBeenCalled()
  })

  it('批量添加:列未用路由 + 默认标题,勾选保存 → 逐个 add(code + type按钮 + parentId)', async () => {
    renderBM() // 无 appPrefix → 不软过滤
    await screen.findByText('GET:/api/v1/sys/user')
    fireEvent.click(screen.getByText('从路由批量添加'))
    // 已用的 GET sys/user 不出现;未用的两条出现
    await waitFor(() => expect(screen.getByText('DELETE /api/v1/sys/user/1')).toBeTruthy())
    expect(screen.getByText('POST /api/v1/biz/order')).toBeTruthy()
    // 勾选 DELETE 行(batchRows 顺序 = 未用路由顺序:DELETE 在前)
    const rowCheckboxes = screen.getAllByRole('checkbox')
    fireEvent.click(rowCheckboxes[0])
    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ })) // antd 两汉字按钮插空格:'保存'→'保 存'
    await waitFor(() => expect(menuApi.add).toHaveBeenCalledWith(expect.objectContaining({
      parentId: 2, type: MenuType.Button, permission: 'DELETE:/api/v1/sys/user/1', title: '删除', moduleId: null,
    })))
    expect(onChanged).toHaveBeenCalled()
  })

  it('批量按应用软过滤:appPrefix=sys 只列本应用路由,biz 隐藏', async () => {
    renderBM('sys')
    await screen.findByText('GET:/api/v1/sys/user')
    fireEvent.click(screen.getByText('从路由批量添加'))
    await waitFor(() => expect(screen.getByText('DELETE /api/v1/sys/user/1')).toBeTruthy())
    expect(screen.queryByText('POST /api/v1/biz/order')).toBeNull() // biz 非本应用 → 默认隐藏
  })

  it('单个新增:填标题保存 → add(type按钮 + moduleId null + parentId=当前菜单)', async () => {
    renderBM()
    await screen.findByText('GET:/api/v1/sys/user')
    fireEvent.click(screen.getByText('新增按钮')) // 触发钮
    // 编辑表单的「标题」占位符唯一(列表弹窗无输入框),据此定位表单,形态(Modal/Drawer)无关。
    const titleInput = await screen.findByPlaceholderText('标题')
    fireEvent.change(titleInput, { target: { value: '导出' } })
    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))
    await waitFor(() => expect(menuApi.add).toHaveBeenCalledWith(expect.objectContaining({
      parentId: 2, type: MenuType.Button, title: '导出', moduleId: null,
    })))
  })
})
