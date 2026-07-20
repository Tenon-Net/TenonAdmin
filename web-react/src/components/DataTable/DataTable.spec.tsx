import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createRef, useEffect, useState } from 'react'
import { render, screen, cleanup, waitFor } from '@testing-library/react'
import type { ProColumns } from '@ant-design/pro-components'

/**
 * **ProTable 被 mock 掉,不渲染真组件。** 原因是 vitest 在导入 `@ant-design/pro-components` 时,
 * 它传递依赖的 `antd/es/locale/zh_CN.js` 用了无扩展名 deep import(`import '../calendar/locale/zh_CN'`),
 * vitest 的 externalize 解析器不补 `.js` → `Cannot find module`(deps.optimizer / inline 几种配置都没解决)。
 * build(rollup)与 dev(Vite)都正常,只有 vitest 的 SSR-externalize 路径缺这一步。
 *
 * 所以这里测的是 **DataTable 的职责 = prop 接线**:request 是不是 `toProTable(fetcher)`、columnsState 键、
 * rowKey、reload 转发、toolbar 渲染。**真 ProTable 的渲染/本地化留 B11(dev 实点)+ E2(CI 冒烟)验**,
 * 不硬扛 vitest 的解析冲突(核心的 `toProTable` 已在 toProTable.spec 里 9 变异钉死)。
 */
let captured: {
  request?: (p: Record<string, unknown>, sort: Record<string, unknown>) => Promise<unknown>
  columnsState?: { persistenceKey?: string; persistenceType?: string }
  rowKey?: string
  toolBarRender?: () => React.ReactNode[]
} = {}

// 假 ProTable:挂载即调 request(证明 fetcher 接对)、渲染返回的行、把 reload 挂到 actionRef。
vi.mock('@ant-design/pro-components', () => ({
  ProTable: (props: Record<string, unknown>) => {
    captured = props as typeof captured
    const request = props.request as (p: Record<string, unknown>, s: Record<string, unknown>) => Promise<{ data: { id: number; name: string }[] }>
    const actionRef = props.actionRef as { current?: { reload: () => void } } | undefined
    const [rows, setRows] = useState<{ id: number; name: string }[]>([])
    const load = () => void request({ current: 1, pageSize: 10 }, {}).then((r) => setRows(r.data))
    useEffect(() => {
      if (actionRef) actionRef.current = { reload: load }
      load()
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [])
    const toolbar = (props.toolBarRender as (() => React.ReactNode[]) | undefined)?.()
    return (
      <div>
        <div data-testid="toolbar">{toolbar}</div>
        {rows.map((r) => (
          <div key={r.id}>{r.name}</div>
        ))}
      </div>
    )
  },
}))

import { DataTable, type DataTableHandle } from './index'

interface Row extends Record<string, unknown> {
  id: number
  name: string
}
const columns: ProColumns<Row>[] = [{ title: '名称', dataIndex: 'name' }]

function mount(props: Partial<Parameters<typeof DataTable<Row>>[0]> = {}, ref?: React.Ref<DataTableHandle>) {
  const fetcher = props.fetcher ?? vi.fn(async () => ({ items: [{ id: 1, name: '张三' }], total: 1 }))
  render(<DataTable<Row> columns={columns} fetcher={fetcher} ref={ref} {...props} />)
  return fetcher
}

beforeEach(() => {
  captured = {}
  localStorage.clear()
})
afterEach(cleanup)

describe('DataTable 接线', () => {
  it('request 经 toProTable 适配 fetcher:拉数据、把 items 渲染成行', async () => {
    const fetcher = vi.fn(async () => ({ items: [{ id: 1, name: '张三' }], total: 1 }))
    mount({ fetcher })
    expect(await screen.findByText('张三')).toBeTruthy()
    // fetcher 收到的是 toProTable 映射后的 {page,pageSize},不是 ProTable 的 {current}。
    expect(fetcher).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 10 }))
  })

  it('persistKey → columnsState 用 localStorage 键 `protable:{key}`', () => {
    mount({ persistKey: 'system-user' })
    expect(captured.columnsState).toEqual({ persistenceKey: 'protable:system-user', persistenceType: 'localStorage' })
  })

  it('不给 persistKey → 不持久化列设置(columnsState undefined)', () => {
    mount()
    expect(captured.columnsState).toBeUndefined()
  })

  it('rowKey 默认 id,可覆写', () => {
    mount()
    expect(captured.rowKey).toBe('id')
    cleanup()
    mount({ rowKey: 'code' })
    expect(captured.rowKey).toBe('code')
  })

  it('reload 句柄 → 重新 fetch(不外泄 ActionType,只给 reload)', async () => {
    const fetcher = vi.fn(async () => ({ items: [{ id: 1, name: '张三' }], total: 1 }))
    const ref = createRef<DataTableHandle>()
    mount({ fetcher }, ref)
    await screen.findByText('张三')
    expect(fetcher).toHaveBeenCalledTimes(1)
    ref.current!.reload()
    await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(2))
  })

  it('toolbar 传入的按钮渲染到工具栏', async () => {
    mount({ toolbar: <button>新增</button> })
    expect(await screen.findByText('新增')).toBeTruthy()
  })
})
