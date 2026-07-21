import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { moduleApi } from '@/api'
import { useAuthStore } from '@/stores/auth'
import type { ModuleRow } from '@/types/api'
import ModulePage from './index'

// module 用原生 antd Table + IconPicker(mock 掉,避免拉图标集),故可真渲染。
const { confirmMock } = vi.hoisted(() => ({ confirmMock: vi.fn() }))
vi.mock('@/hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: confirmMock, run: vi.fn(), ask: vi.fn() }) }))
vi.mock('@/components/IconPicker', () => ({ IconPicker: () => <div data-testid="icon-picker" /> }))

const ROWS: ModuleRow[] = [
  { id: 1, code: 'system', title: '系统', icon: null, defaultRoute: '/system', apiPrefix: 'sys', sort: 0, enabled: true, remark: null },
  { id: 2, code: 'biz', title: '业务', icon: 'ph:cube', defaultRoute: '/biz', apiPrefix: 'biz', sort: 1, enabled: false, remark: 'r' },
]
const mount = () => render(<AntdApp><ModulePage /></AntdApp>)

beforeEach(() => {
  confirmMock.mockReset()
  confirmMock.mockImplementation(async (o: { action: () => Promise<unknown> }) => { await o.action(); return true })
  vi.spyOn(moduleApi, 'list').mockResolvedValue(ROWS)
  vi.spyOn(moduleApi, 'add').mockResolvedValue(88)
  vi.spyOn(moduleApi, 'update').mockResolvedValue(true)
  vi.spyOn(moduleApi, 'remove').mockResolvedValue(true)
  useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
  vi.restoreAllMocks()
})

describe('ModulePage', () => {
  it('load 渲染应用行 + 内置标签', async () => {
    mount()
    expect(await screen.findByText('system')).toBeTruthy()
    expect(screen.getByText('业务')).toBeTruthy() // 'biz' 在 code 与 apiPrefix 两列重复 → 用唯一 title 定位
    expect(screen.getByText('内置')).toBeTruthy() // system 内置标签
  })

  it('内置 system:状态开关置灰 + 无删除按钮', async () => {
    mount()
    await screen.findByText('system')
    const switches = screen.getAllByRole('switch') // [system, biz]
    expect((switches[0] as HTMLButtonElement).disabled).toBe(true) // 内置禁停
    expect((switches[1] as HTMLButtonElement).disabled).toBe(false)
    expect(screen.getAllByText('删除').length).toBe(1) // 仅 biz 可删(内置禁删)
  })

  it('状态开关(非内置)走全量 update', async () => {
    mount()
    await screen.findByText('业务')
    fireEvent.click(screen.getAllByRole('switch')[1]) // biz enabled false→true(启用无需确认)
    await waitFor(() => expect(moduleApi.update).toHaveBeenCalledWith(2, {
      code: 'biz', title: '业务', icon: 'ph:cube', defaultRoute: '/biz', apiPrefix: 'biz', sort: 1, enabled: true, remark: 'r',
    }))
  })

  it('删除(非内置)走 confirm → remove', async () => {
    mount()
    await screen.findByText('业务')
    fireEvent.click(screen.getByText('删除'))
    await waitFor(() => expect(moduleApi.remove).toHaveBeenCalledWith(2))
  })

  it('新增:提交全部字段(save 路径 —— 吸取 C9 教训,弹窗 save 必测)', async () => {
    mount()
    await screen.findByText('system')
    fireEvent.click(screen.getByRole('button', { name: /新\s*增/ }))
    fireEvent.change(await screen.findByPlaceholderText('编码'), { target: { value: 'hr' } })
    fireEvent.change(screen.getByPlaceholderText('名称'), { target: { value: '人事' } })
    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))
    await waitFor(() => expect(moduleApi.add).toHaveBeenCalledWith({
      code: 'hr', title: '人事', icon: '', defaultRoute: '', apiPrefix: '', sort: 0, enabled: true, remark: '',
    }))
  })

  it('无 PUT 权限:退化只读状态标签(无开关)、无编辑', async () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await screen.findByText('system')
    expect(screen.queryAllByRole('switch').length).toBe(0) // 无开关
    expect(screen.queryByText('编辑')).toBeNull()
    expect(screen.getAllByText('启用').length).toBeGreaterThan(0) // system 只读「启用」标签
  })
})
