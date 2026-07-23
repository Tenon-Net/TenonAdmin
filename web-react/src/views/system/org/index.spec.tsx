import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { type ReactElement, type ReactNode } from 'react'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import type { ProColumns } from '@ant-design/pro-components'
import '@/locales'
import { orgApi } from '@/api'
import { useAuthStore } from '@/stores/auth'
import type { SysOrg } from '@/types/api'

const { confirmMock, runMock } = vi.hoisted(() => ({ confirmMock: vi.fn(), runMock: vi.fn() }))
vi.mock('@/hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: confirmMock, run: runMock, ask: vi.fn() }) }))

// mock TreeTable:捕获 columns/data/expandedRowKeys/toolbar(树表渲染撞 pro-components 墙,只测接线)。
let captured: { columns?: ProColumns<SysOrg>[]; data?: SysOrg[]; expandedRowKeys?: number[]; toolbar?: ReactNode } = {}
vi.mock('@/components/TreeTable', () => ({
  TreeTable: (props: typeof captured) => { captured = props; return <div data-testid="tt">{props.toolbar}</div> },
}))
// 这几个子件挂载会拉 orgApi/dict:mock 成占位,本页测的是接线而非子件本身。
vi.mock('@/components/OrgTreeSelect', () => ({ OrgTreeSelect: () => <div data-testid="org-tree-select" /> }))
vi.mock('@/components/DictSelect', () => ({ DictSelect: () => <div data-testid="dict-select" /> }))
vi.mock('@/components/DictTag', () => ({ DictTag: ({ value }: { value?: string | null }) => <span>{value ?? '—'}</span> }))

import OrgPage from './index'

const FLAT: SysOrg[] = [
  { id: 1, parentId: 0, name: '总公司', code: 'HQ', category: null, sort: 0, enabled: true },
  { id: 2, parentId: 1, name: '研发部', code: 'RD', category: 'dept', sort: 1, enabled: true },
  { id: 3, parentId: 0, name: '分公司', code: 'BR', category: null, sort: 2, enabled: false },
]
const mount = () => render(<AntdApp><OrgPage /></AntdApp>)
type AnyEl = ReactElement<Record<string, any>>
const findCol = (key: string) => captured.columns!.find((c) => c.dataIndex === key || c.key === key)!
const callRender = (c: ProColumns<SysOrg>, r: SysOrg): AnyEl =>
  (c.render as (d: unknown, e: SysOrg) => AnyEl)(null, r)

beforeEach(() => {
  captured = {}
  confirmMock.mockReset(); runMock.mockReset()
  confirmMock.mockImplementation(async (o: { action: () => Promise<unknown> }) => { await o.action(); return true })
  runMock.mockImplementation(async (action: () => Promise<unknown>) => { await action(); return true })
  vi.spyOn(orgApi, 'list').mockResolvedValue(FLAT)
  vi.spyOn(orgApi, 'update').mockResolvedValue(true)
  vi.spyOn(orgApi, 'remove').mockResolvedValue(true)
  vi.spyOn(orgApi, 'copy').mockResolvedValue(99)
  vi.spyOn(orgApi, 'add').mockResolvedValue(88)
  useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
  vi.restoreAllMocks()
})

describe('OrgPage 接线', () => {
  it('load:orgApi.list → buildTree 拼树 + 播种展开(仅有子节点的 id)', async () => {
    mount()
    await waitFor(() => expect(captured.data?.length).toBe(2)) // 两个根:总公司/分公司
    const hq = captured.data!.find((n) => n.id === 1) as SysOrg & { children: SysOrg[] }
    expect(hq.children.map((c) => c.id)).toEqual([2]) // 研发部挂总公司下
    await waitFor(() => expect(captured.expandedRowKeys).toEqual([1])) // 只有总公司有子 → 播种展开它
  })

  it('关键字搜索:过滤到命中节点(连祖先链)', async () => {
    mount()
    await waitFor(() => expect(captured.data?.length).toBe(2))
    fireEvent.change(screen.getByPlaceholderText('搜索机构名称/编码'), { target: { value: '研发' } })
    await waitFor(() => expect(captured.data?.length).toBe(1)) // 只剩 总公司>研发部 这一支
    const branch = captured.data![0] as SysOrg & { children: SysOrg[] }
    expect(branch.id).toBe(1)
    expect(branch.children[0].id).toBe(2)
  })

  it('StatusSwitch:请求走全量 update(全字段 + enabled),不漏字段抹空', async () => {
    mount()
    await waitFor(() => expect(captured.columns).toBeTruthy())
    const el = callRender(findCol('enabled'), FLAT[1]) // 研发部
    await el.props.request(false)
    expect(orgApi.update).toHaveBeenCalledWith(2, { parentId: 1, name: '研发部', code: 'RD', category: 'dept', sort: 1, enabled: false })
  })

  it('StatusSwitch:无 PUT 权限置灰', async () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await waitFor(() => expect(captured.columns).toBeTruthy())
    expect(callRender(findCol('enabled'), FLAT[0]).props.disabled).toBe(true)
  })

  it('操作列 更多▾:超管含 新增下级/复制/删除;删除走 confirm → remove + 重拉', async () => {
    mount()
    await waitFor(() => expect(captured.columns).toBeTruthy())
    const cell = callRender(captured.columns!.find((c) => c.key === 'op')!, FLAT[2]) // 分公司 id3
    const dropdown = cell.props.children[1] as AnyEl
    expect(dropdown.props.menu.items.map((i: { key: string }) => i.key)).toEqual(['addChild', 'copy', 'delete'])
    dropdown.props.menu.onClick({ key: 'delete' })
    await waitFor(() => expect(orgApi.remove).toHaveBeenCalledWith(3))
    await waitFor(() => expect(vi.mocked(orgApi.list).mock.calls.length).toBeGreaterThan(1)) // 删后重拉
  })

  it('操作列:无写权限 → 无编辑钮、无更多下拉', async () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: [] })
    mount()
    await waitFor(() => expect(captured.columns).toBeTruthy())
    const cell = callRender(captured.columns!.find((c) => c.key === 'op')!, FLAT[0])
    expect(cell.props.children[0]).toBeFalsy() // 编辑钮
    expect(cell.props.children[1]).toBeFalsy() // 更多下拉
  })
})
