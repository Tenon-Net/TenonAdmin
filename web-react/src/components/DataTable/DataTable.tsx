import { forwardRef, useImperativeHandle, useRef, type ReactNode } from 'react'
import { ProTable } from '@ant-design/pro-components'
import type { ActionType, ProColumns } from '@ant-design/pro-components'
import { toProTable, type PageFetcher } from './toProTable'

/**
 * CRUD 列表页的薄封装。**隔离 `@ant-design/pro-components`(beta)**:16 个 CRUD 页只依赖本组件 +
 * `toProTable`,不直接碰 ProTable 的 API。将来 pro-components 换版/换库,改这一处。
 *
 * 工具栏文案(列设置/密度/刷新/搜索/重置)由 **pro-components 自带的 intl** 提供,跟随 `App.tsx` 的
 * `ConfigProvider locale` 自动切中英 —— 所以**不新增 `proTable.*` i18n 键**(Vue 侧那 8 个是 Naive
 * ProTable 的自留文案,antd ProTable 用不上)。列标题等业务文案由各页 `columns` 的 `title` 自己 `t()`。
 */
export interface DataTableProps<T extends Record<string, unknown>> {
  columns: ProColumns<T>[]
  /** `(params)=>{items,total}`;经 `toProTable` 适配成 ProTable 的 request 契约。 */
  fetcher: PageFetcher<T>
  /** 列设置持久化键 → `localStorage['protable:{persistKey}']`;不给则列设置不持久化。 */
  persistKey?: string
  rowKey?: string
  /** 工具栏右侧按钮(新增 / 批量删除等)。 */
  toolbar?: ReactNode
  headerTitle?: ReactNode
}

/** 暴露给调用方的句柄(增删改后刷新)——只给 `reload`,不外泄 pro-components 的 `ActionType`。 */
export interface DataTableHandle {
  reload: () => void
}

function DataTableInner<T extends Record<string, unknown>>(
  { columns, fetcher, persistKey, rowKey = 'id', toolbar, headerTitle }: DataTableProps<T>,
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
      columnsState={persistKey ? { persistenceKey: `protable:${persistKey}`, persistenceType: 'localStorage' } : undefined}
      search={{ labelWidth: 'auto' }}
      pagination={{ showSizeChanger: true, defaultPageSize: 10 }}
      headerTitle={headerTitle}
      toolBarRender={() => (toolbar ? [toolbar] : [])}
      dateFormatter="string"
    />
  )
}

// forwardRef + 泛型:cast 回带泛型的签名(forwardRef 会擦掉泛型)。这是 React 泛型 forwardRef 的标准写法,不是抹类型。
export const DataTable = forwardRef(DataTableInner) as <T extends Record<string, unknown>>(
  props: DataTableProps<T> & { ref?: React.ForwardedRef<DataTableHandle> },
) => ReturnType<typeof DataTableInner>
