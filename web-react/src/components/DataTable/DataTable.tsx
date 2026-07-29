import { forwardRef, useImperativeHandle, useRef, type Key, type ReactNode } from 'react'
import type { TableProps } from 'antd'
import { ProTable } from '@ant-design/pro-components'
import type { ActionType, ProColumns } from '@ant-design/pro-components'
import { toProTable, type PageFetcher } from './toProTable'
import './DataTable.css'

/**
 * CRUD 列表页的薄封装。**隔离 `@ant-design/pro-components`(beta)**:16 个 CRUD 页只依赖本组件 +
 * `toProTable`,不直接碰 ProTable 的 API。将来 pro-components 换版/换库,改这一处。
 *
 * 工具栏文案(列设置/密度/刷新/搜索/重置)由 **pro-components 自带的 intl** 提供,跟随 `App.tsx` 的
 * `ConfigProvider locale` 自动切中英 —— 所以**不新增 `proTable.*` i18n 键**(Vue 侧那 8 个是 Naive
 * ProTable 的自留文案,antd ProTable 用不上)。列标题等业务文案由各页 `columns` 的 `title` 自己 `t()`。
 */
// 约束用 `Record<string, any>` 而非 `unknown`:业务实体是 interface(如 `UserItem`),**接口没有隐式索引签名**,
// `Record<string, unknown>` 会拒收(B11 第一个真消费者踩到);`Record<string, any>` 才收接口,ProTable 自身也是 `<T = any>`。
export interface DataTableProps<T extends Record<string, any>> {
  columns: ProColumns<T>[]
  /** `(params)=>{items,total}`;经 `toProTable` 适配成 ProTable 的 request 契约。 */
  fetcher: PageFetcher<T>
  /** 列设置持久化键 → `localStorage['protable:{persistKey}']`;不给则列设置不持久化。 */
  persistKey?: string
  rowKey?: string
  /**
   * 工具栏主操作(新增 / 批量删除等)。挂在**左侧**(`headerTitle`),对齐 Vue ProTable 的 `#toolbar`;
   * 右侧留给 pro-components 自带的刷新/密度/列设置,不再把业务按钮塞进 `toolBarRender`。
   */
  toolbar?: ReactNode
  /**
   * 受控行选择(勾选列 + 选中态)。透传给内层 ProTable —— 批量删除页配 useBatchDelete 用:
   * `rowSelection={{ selectedRowKeys, onChange }}`。不给则不显示勾选列(默认无选择)。
   */
  rowSelection?: TableProps<T>['rowSelection']
  /** 行点击(主从页左栏选中一行 → 加载右栏)。给了才有指针手型 + onClick;行内控件须自行 stopPropagation 防冒泡。 */
  onRowClick?: (record: T) => void
  /** 选中行高亮:rowKey 命中此值的行套 `.data-table-active-row`(见 DataTable.css)。主从页配 onRowClick 用。 */
  activeRowKey?: Key | null
  /**
   * 透传给 ProTable 的 `params`:变化即自动 reload 回第 1 页。侧栏/主从筛选用
   * (如用户页左机构树 → `params={{ orgId }}`)。ProTable 深比较,传新对象字面量不会自旋。
   */
  params?: Record<string, unknown>
}

/** 暴露给调用方的句柄(增删改后刷新)——只给 `reload`,不外泄 pro-components 的 `ActionType`。 */
export interface DataTableHandle {
  reload: () => void
}

function DataTableInner<T extends Record<string, any>>(
  { columns, fetcher, persistKey, rowKey = 'id', toolbar, rowSelection, onRowClick, activeRowKey, params }: DataTableProps<T>,
  ref: React.ForwardedRef<DataTableHandle>,
) {
  const actionRef = useRef<ActionType | undefined>(undefined)
  useImperativeHandle(ref, () => ({ reload: () => actionRef.current?.reload() }), [])

  return (
    <ProTable<T>
      actionRef={actionRef}
      columns={columns}
      request={toProTable(fetcher)}
      rowKey={rowKey}
      rowSelection={rowSelection}
      onRow={onRowClick ? (record) => ({ onClick: () => onRowClick(record), style: { cursor: 'pointer' } }) : undefined}
      rowClassName={activeRowKey == null ? undefined : (record) => (record[rowKey] === activeRowKey ? 'data-table-active-row' : '')}
      columnsState={persistKey ? { persistenceKey: `protable:${persistKey}`, persistenceType: 'localStorage' } : undefined}
      params={params}
      search={{ labelWidth: 'auto' }}
      // 搜索区与表格分成两张有边框卡(对齐 Vue ProTable 的 .pro-table-search / .pro-table-card);
      // 不开则两块无边框区浮在页面底色上、视觉连成一片。
      cardBordered
      pagination={{ showSizeChanger: true, defaultPageSize: 10 }}
      // 左业务钮、右设置钮:headerTitle 在 ListToolBar 左侧,对齐 Vue #toolbar。
      // 勿用 toolBarRender 放业务钮——那是右侧 actions,和 Vue 对不齐。
      headerTitle={toolbar}
      dateFormatter="string"
    />
  )
}

// forwardRef + 泛型:cast 回带泛型的签名(forwardRef 会擦掉泛型)。这是 React 泛型 forwardRef 的标准写法,不是抹类型。
export const DataTable = forwardRef(DataTableInner) as <T extends Record<string, any>>(
  props: DataTableProps<T> & { ref?: React.ForwardedRef<DataTableHandle> },
) => ReturnType<typeof DataTableInner>
