import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, waitFor, fireEvent, act } from '@testing-library/react'
import { createRef } from 'react'
import { App as AntdApp } from 'antd'
import '@/locales' // t() 要真文案
import {
  UserPicker, type UserPickerHandle,
  orgIdFromKey, addUsers, removeUser, filterSelected, hydrateSelected, type PickedUser,
} from './UserPicker'

// DataTable 静态导入 ProTable(pro-components 在 vitest 解析不了),mock 掉、捕获 UserPicker 传下去的
// columns/fetcher,单独验接线(与 B11 index.spec 同款)。真表格的列表/持久化留 dev 实点。
let captured: { fetcher?: (q: Record<string, unknown>) => Promise<unknown>; columns?: Array<Record<string, unknown>> } = {}
vi.mock('@/components/DataTable', () => ({
  DataTable: (p: { fetcher?: (q: Record<string, unknown>) => Promise<unknown>; columns?: Array<Record<string, unknown>> }) => {
    captured = p
    return <div data-testid="dt" />
  },
}))
vi.mock('@/api', () => ({ orgApi: { list: vi.fn() }, userApi: { page: vi.fn() } }))

import { orgApi, userApi } from '@/api'
import type { UserItem } from '@/types/api'

const U = (id: number, name: string, account: string): PickedUser => ({ id, name, account })
// userApi.page 的返回项是完整 UserItem;测试里只关心这三个字段,其余 cast 掉。
const row = (id: number, name: string, account: string) => ({ id, name, account }) as unknown as UserItem

function mount(props: { excludeIds?: number[]; onConfirm: (ids: number[]) => void }) {
  const ref = createRef<UserPickerHandle>()
  render(
    <AntdApp>
      <UserPicker ref={ref} {...props} />
    </AntdApp>,
  )
  return ref
}
/** columns 的操作列 render 返回的 <Button>(取 props 直接看,不必挂树)。 */
const opButton = (r: PickedUser) => {
  const op = captured.columns!.find((c) => c.key === 'op') as { render: (d: unknown, r: unknown) => { props: { disabled: boolean; onClick: () => void } } }
  return op.render(null, r)
}

beforeEach(() => {
  vi.mocked(orgApi.list).mockResolvedValue([])
  vi.mocked(userApi.page).mockResolvedValue({ items: [], total: 0 })
})
afterEach(() => {
  cleanup()
  captured = {}
  vi.mocked(orgApi.list).mockReset()
  vi.mocked(userApi.page).mockReset()
})

// ── 纯逻辑 ──
describe('orgIdFromKey', () => {
  it('根(0)/未选(null)→ undefined;其余→原值', () => {
    expect(orgIdFromKey(0)).toBeUndefined()
    expect(orgIdFromKey(null)).toBeUndefined()
    expect(orgIdFromKey(5)).toBe(5)
  })
})

describe('addUsers', () => {
  it('去重:已在 current 的不重复加', () => {
    expect(addUsers([U(1, '甲', 'a')], [U(1, '甲', 'a'), U(2, '乙', 'b')], new Set()).map((u) => u.id)).toEqual([1, 2])
  })
  it('exclude 里的不加', () => {
    expect(addUsers([], [U(1, '甲', 'a'), U(2, '乙', 'b')], new Set([2])).map((u) => u.id)).toEqual([1])
  })
  it('不改原数组', () => {
    const cur = [U(1, '甲', 'a')]
    addUsers(cur, [U(2, '乙', 'b')], new Set())
    expect(cur.map((u) => u.id)).toEqual([1])
  })
})

describe('removeUser', () => {
  it('按 id 移除', () => {
    expect(removeUser([U(1, '甲', 'a'), U(2, '乙', 'b')], 1).map((u) => u.id)).toEqual([2])
  })
})

describe('filterSelected', () => {
  it('空/纯空白 query 返回原列表', () => {
    const list = [U(1, '甲', 'a'), U(2, '乙', 'b')]
    expect(filterSelected(list, '  ')).toBe(list)
  })
  it('按姓名或账号大小写不敏感包含', () => {
    const list = [U(1, '甲', 'a'), U(3, '张三', 'ZhangSan')]
    expect(filterSelected(list, 'zhang').map((u) => u.id)).toEqual([3]) // 账号命中(大小写不敏感)
    expect(filterSelected(list, '张').map((u) => u.id)).toEqual([3]) // 姓名命中
  })
})

describe('hydrateSelected', () => {
  it('找得到用真数据,找不到退占位(只显 id),按 ids 顺序', () => {
    expect(hydrateSelected([2, 9], [U(2, '乙', 'b')])).toEqual([
      { id: 2, name: '乙', account: 'b' },
      { id: 9, name: '9', account: '9' },
    ])
  })
})

// ── 组件接线 ──
describe('UserPicker 接线', () => {
  it('open() 打开弹窗', async () => {
    const ref = mount({ onConfirm: vi.fn() })
    await act(async () => { ref.current!.open() })
    expect(screen.getByText('选择用户')).toBeTruthy()
  })

  it('open(ids) → hydrate 进已选 → 确认回调收到这些 id', async () => {
    vi.mocked(userApi.page).mockResolvedValue({ items: [row(2, '乙', 'b')], total: 1 })
    const onConfirm = vi.fn()
    const ref = mount({ onConfirm })
    await act(async () => { await ref.current!.open([2, 9]) })
    fireEvent.click(screen.getByRole('button', { name: /确\s*定/ }))
    await waitFor(() => expect(onConfirm).toHaveBeenCalledWith([2, 9]))
  })

  it('中间表 fetcher:透传分页/搜索,orgId 初始 undefined', async () => {
    const ref = mount({ onConfirm: vi.fn() })
    await act(async () => { ref.current!.open() })
    await captured.fetcher!({ page: 1, pageSize: 10, name: '张' })
    expect(userApi.page).toHaveBeenCalledWith({ page: 1, pageSize: 10, name: '张', orgId: undefined })
  })

  it('exclude 的行「添加」置灰,普通行可点', async () => {
    const ref = mount({ excludeIds: [7], onConfirm: vi.fn() })
    await act(async () => { ref.current!.open() })
    expect(opButton(U(7, 'x', 'x')).props.disabled).toBe(true)
    expect(opButton(U(8, 'y', 'y')).props.disabled).toBe(false)
  })

  it('点行内「添加」→ 进已选,确认带上该 id', async () => {
    const onConfirm = vi.fn()
    const ref = mount({ onConfirm })
    await act(async () => { ref.current!.open() })
    act(() => { opButton(U(5, '张三', 'zs')).props.onClick() })
    fireEvent.click(screen.getByRole('button', { name: /确\s*定/ }))
    await waitFor(() => expect(onConfirm).toHaveBeenCalledWith([5]))
  })
})
