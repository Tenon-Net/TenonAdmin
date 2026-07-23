import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { act, type ReactElement, type ReactNode } from 'react'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import type { ProColumns } from '@ant-design/pro-components'
import '@/locales'
import { dictAdminApi } from '@/api'
import { useAuthStore } from '@/stores/auth'
import { useDictStore } from '@/stores/dict'
import type { SysDictItem, SysDictType } from '@/types/api'

const { confirmMock, invalidateMock, reloadMock } = vi.hoisted(() => ({ confirmMock: vi.fn(), invalidateMock: vi.fn(), reloadMock: vi.fn() }))
vi.mock('@/hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: confirmMock, run: vi.fn(), ask: vi.fn() }) }))

// mock DataTable(撞 pro-components 墙,只测接线):捕获 props(columns/onRowClick/activeRowKey/rowSelection/toolbar),
// 用 forwardRef 暴露 reload=reloadMock 以断言重载。右侧字典项表是裸 antd Table,真渲染。
let dt: { columns?: ProColumns<SysDictType>[]; onRowClick?: (r: SysDictType) => void; activeRowKey?: number | null; rowSelection?: { onChange?: (k: unknown) => void }; toolbar?: ReactNode } = {}
vi.mock('@/components/DataTable', async () => {
  const { forwardRef, useImperativeHandle } = await import('react')
  return {
    DataTable: forwardRef(function MockDT(props: typeof dt, ref: React.Ref<{ reload: () => void }>) {
      dt = props
      useImperativeHandle(ref, () => ({ reload: reloadMock }))
      return <div data-testid="dt">{props.toolbar}</div>
    }),
  }
})

import DictPage from './index'

const TYPES: SysDictType[] = [
  { id: 1, code: 'sex', name: '性别', sort: 0, enabled: true, remark: null },
  { id: 2, code: 'status', name: '状态', sort: 1, enabled: false, remark: 'x' },
]
const ITEMS_SEX: SysDictItem[] = [
  { id: 11, dictTypeCode: 'sex', label: '男', value: '1', sort: 0, enabled: true },
  { id: 12, dictTypeCode: 'sex', label: '女', value: '2', sort: 1, enabled: false },
]
const ITEMS_STATUS: SysDictItem[] = [{ id: 21, dictTypeCode: 'status', label: '启用', value: '1', sort: 0, enabled: true }]

const mount = () => render(<AntdApp><DictPage /></AntdApp>)
type AnyEl = ReactElement<Record<string, any>>
const col = (key: string) => dt.columns!.find((c) => c.dataIndex === key || c.key === key)!
const callRender = (c: ProColumns<SysDictType>, r: SysDictType): AnyEl => (c.render as (d: unknown, e: SysDictType) => AnyEl)(null, r)

beforeEach(() => {
  dt = {}
  confirmMock.mockReset(); invalidateMock.mockReset(); reloadMock.mockReset()
  confirmMock.mockImplementation(async (o: { action: () => Promise<unknown> }) => { await o.action(); return true })
  vi.spyOn(dictAdminApi, 'items').mockImplementation((code: string) => Promise.resolve(code === 'sex' ? ITEMS_SEX : ITEMS_STATUS))
  vi.spyOn(dictAdminApi, 'typeUpdate').mockResolvedValue(true)
  vi.spyOn(dictAdminApi, 'typeRemove').mockResolvedValue(true)
  vi.spyOn(dictAdminApi, 'itemUpdate').mockResolvedValue(true)
  vi.spyOn(dictAdminApi, 'itemRemove').mockResolvedValue(true)
  useDictStore.setState({ invalidate: invalidateMock })
  useAuthStore.setState({ isSuperAdmin: true, permissionsLoaded: true, permissionCodes: [] })
})
afterEach(() => {
  cleanup()
  useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: [] })
  vi.restoreAllMocks()
})

describe('DictPage 主从接线', () => {
  it('行点击 → selectType:拉该类型项 + activeRowKey 高亮 + 右栏渲染项', async () => {
    mount()
    await act(async () => { dt.onRowClick!(TYPES[0]) })
    expect(dictAdminApi.items).toHaveBeenCalledWith('sex')
    await waitFor(() => expect(screen.getByText('男')).toBeTruthy())
    expect(screen.getByText('女')).toBeTruthy()
    expect(dt.activeRowKey).toBe(1) // 选中高亮
  })

  it('竞态守卫:切类型时过期响应不覆盖当前项', async () => {
    let resolveSex: (v: SysDictItem[]) => void = () => {}
    vi.mocked(dictAdminApi.items).mockImplementation((code: string) =>
      code === 'sex' ? new Promise<SysDictItem[]>((res) => { resolveSex = res }) : Promise.resolve(ITEMS_STATUS))
    mount()
    await act(async () => {
      dt.onRowClick!(TYPES[0]) // 发起 sex(挂起)
      dt.onRowClick!(TYPES[1]) // 发起 status(立即)
    })
    await waitFor(() => expect(screen.getByText('启用')).toBeTruthy()) // status 项已渲染
    await act(async () => { resolveSex(ITEMS_SEX) }) // sex 迟到 resolve
    expect(screen.queryByText('男')).toBeNull() // 过期响应被守卫丢弃,不覆盖 status
    expect(screen.getByText('启用')).toBeTruthy()
  })

  it('类型状态列:stopPropagation + 全量 update + invalidate + 重载', async () => {
    mount()
    const el = callRender(col('enabled'), TYPES[0])
    const ev = { stopPropagation: vi.fn() }
    el.props.onClick(ev as unknown as Event)
    expect(ev.stopPropagation).toHaveBeenCalled() // 开关点击不冒泡到行(否则误切选中类型)
    const sw = el.props.children as AnyEl
    await sw.props.request(false)
    expect(dictAdminApi.typeUpdate).toHaveBeenCalledWith(1, { code: 'sex', name: '性别', sort: 0, enabled: false, remark: '' })
    sw.props.onChange(false)
    expect(invalidateMock).toHaveBeenCalledWith('sex')
    expect(reloadMock).toHaveBeenCalled()
  })

  it('类型状态列:无 PUT 权限置灰', () => {
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: [] })
    mount()
    expect((callRender(col('enabled'), TYPES[0]).props.children as AnyEl).props.disabled).toBe(true)
  })

  it('类型操作列:stopPropagation + 删除走 confirm → remove + invalidate;无权限则空', async () => {
    mount()
    const cell = callRender(col('op'), TYPES[1]) // status id2
    const ev = { stopPropagation: vi.fn() }
    cell.props.onClick(ev as unknown as Event)
    expect(ev.stopPropagation).toHaveBeenCalled()
    const [editBtn, delBtn] = (cell.props.children as AnyEl).props.children as [AnyEl, AnyEl]
    expect(editBtn).toBeTruthy()
    delBtn.props.onClick()
    await waitFor(() => expect(dictAdminApi.typeRemove).toHaveBeenCalledWith(2))
    expect(invalidateMock).toHaveBeenCalledWith('status')
    // 无写权限 → 编辑/删除都不出
    cleanup()
    useAuthStore.setState({ isSuperAdmin: false, permissionsLoaded: true, permissionCodes: [] })
    mount()
    const [e2, d2] = (callRender(col('op'), TYPES[0]).props.children as AnyEl).props.children as [AnyEl, AnyEl]
    expect(e2).toBeFalsy()
    expect(d2).toBeFalsy()
  })

  it('字典项:状态切换走全量 update + invalidate;删除走 confirm → itemRemove', async () => {
    mount()
    await act(async () => { dt.onRowClick!(TYPES[0]) })
    await waitFor(() => expect(screen.getByText('男')).toBeTruthy())
    // 项状态开关(右栏 2 个;类型表被 mock 无开关)
    fireEvent.click(screen.getAllByRole('switch')[0]) // 男(enabled→false)
    await waitFor(() => expect(dictAdminApi.itemUpdate).toHaveBeenCalledWith(11, { dictTypeCode: 'sex', label: '男', value: '1', sort: 0, enabled: false }))
    expect(invalidateMock).toHaveBeenCalledWith('sex')
    // 删除首项
    fireEvent.click(screen.getAllByText('删除')[0])
    await waitFor(() => expect(dictAdminApi.itemRemove).toHaveBeenCalledWith(11))
  })

  it('未选类型 → 右栏空态提示,不渲染项表', () => {
    mount()
    expect(screen.getByText('请选择左侧字典类型以管理其字典项')).toBeTruthy()
  })

  it('工具栏:超管可见 新增/批量删除;批量删除默认禁用', () => {
    mount()
    expect(screen.getByRole('button', { name: /新\s*增/ })).toBeTruthy() // antd 两汉字按钮插空格:'新增'→'新 增'
    const batch = screen.getByText('批量删除').closest('button')!
    expect(batch.disabled).toBe(true) // 未勾选 → 禁用
  })

  // ── 弹窗 save 路径(C9 review 补:HIGH 缺陷正藏在这条未测接缝里)──
  it('新增字典项:提交带上隐藏 FK dictTypeCode(漏则建孤儿项 / 编辑摘除)', async () => {
    vi.spyOn(dictAdminApi, 'itemAdd').mockResolvedValue(99)
    mount()
    await act(async () => { dt.onRowClick!(TYPES[0]) }) // 选中 sex
    await waitFor(() => expect(screen.getByText('男')).toBeTruthy())
    fireEvent.click(screen.getByText('新增字典项'))
    fireEvent.change(await screen.findByPlaceholderText('显示文本'), { target: { value: '未知' } })
    fireEvent.change(screen.getByPlaceholderText('值'), { target: { value: '0' } })
    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))
    await waitFor(() => expect(dictAdminApi.itemAdd).toHaveBeenCalledWith(expect.objectContaining({
      dictTypeCode: 'sex', label: '未知', value: '0', sort: 0, enabled: true,
    })))
  })

  it('新增字典类型:提交全部字段(code/name/sort/enabled/remark)', async () => {
    vi.spyOn(dictAdminApi, 'typeAdd').mockResolvedValue(88)
    mount()
    fireEvent.click(screen.getByRole('button', { name: /新\s*增/ })) // 类型工具栏新增(未选类型 → 无「新增字典项」歧义)
    fireEvent.change(await screen.findByPlaceholderText('类型编码'), { target: { value: 'lang' } })
    fireEvent.change(screen.getByPlaceholderText('类型名称'), { target: { value: '语言' } })
    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))
    await waitFor(() => expect(dictAdminApi.typeAdd).toHaveBeenCalledWith({ code: 'lang', name: '语言', sort: 0, enabled: true, remark: '' }))
  })
})
