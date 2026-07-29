import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import type { ProColumns } from '@ant-design/pro-components'

/**
 * 同 DataTable.spec:**ProTable 被 mock 掉**(vitest 撞 pro-components 的无扩展名 deep-import 墙)。
 * 这里测的是 TreeTable 的职责 = **静态/树形态的 prop 接线**:dataSource、search/pagination 关、options.reload 关、
 * 受控 expandable + onExpandedRowsChange 适配、columnsState 键、rowKey、toolbar。真 ProTable 渲染留 dev/E2 验。
 */
let captured: Record<string, any> = {}
vi.mock('@ant-design/pro-components', () => ({
  ProTable: (props: Record<string, unknown>) => {
    captured = props as Record<string, any>
    return <div data-testid="pt">{props.headerTitle as React.ReactNode}</div>
  },
}))

import { TreeTable } from './TreeTable'

interface Row extends Record<string, unknown> { id: number; name: string; children?: Row[] }
const columns: ProColumns<Row>[] = [{ title: '名称', dataIndex: 'name' }]
const data: Row[] = [{ id: 1, name: 'A', children: [{ id: 2, name: 'B' }] }]

function mount(props: Partial<Parameters<typeof TreeTable<Row>>[0]> = {}) {
  const onExp = props.onExpandedRowKeysChange ?? vi.fn()
  render(<TreeTable<Row> columns={columns} data={data} expandedRowKeys={[1]} onExpandedRowKeysChange={onExp} {...props} />)
  return onExp
}

beforeEach(() => { captured = {}; localStorage.clear() })
afterEach(cleanup)

describe('TreeTable 接线', () => {
  it('静态模式:dataSource 转发、search/pagination 关、options.reload 关', () => {
    mount()
    expect(captured.dataSource).toBe(data)
    expect(captured.search).toBe(false)
    expect(captured.pagination).toBe(false)
    expect(captured.options).toMatchObject({ reload: false })
  })

  it('受控展开:expandedRowKeys 转发;onExpandedRowsChange 回调适配 → onExpandedRowKeysChange', () => {
    const onExp = mount()
    expect(captured.expandable.expandedRowKeys).toEqual([1])
    captured.expandable.onExpandedRowsChange([1, 2])
    expect(onExp).toHaveBeenCalledWith([1, 2])
  })

  it('persistKey → columnsState 用 protable:{key};不给则 undefined', () => {
    mount({ persistKey: 'sys-org' })
    expect(captured.columnsState).toEqual({ persistenceKey: 'protable:sys-org', persistenceType: 'localStorage' })
    cleanup()
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

  it('toolbar 挂到 headerTitle(左侧,对齐 Vue #toolbar)', () => {
    mount({ toolbar: <button>新增</button> })
    expect(captured.headerTitle).toBeTruthy()
    expect(screen.getByText('新增')).toBeTruthy()
  })
})
