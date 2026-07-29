import { type Key, type ReactNode } from 'react'
import { ProTable, type ProColumns } from '@ant-design/pro-components'

/**
 * 树表薄封装 —— **隔离 `@ant-design/pro-components`(beta)** 的**另一形态**:静态 `dataSource` + 受控展开,
 * 无分页、无 `request`、无内建搜索表单。`DataTable` 是 fetcher(请求)模式;树页(org/menu)是静态树:
 * 平铺 → `buildTree` 拼 `children`,ProTable/antd Table 按 `childrenColumnName='children'` 自动嵌套渲染。
 *
 * **搜索/展开/新增等工具栏由各页自管**:树表无分页,ProTable 自带的搜索卡片会白占一整块高度,
 * 且「默认全展开」需调用方自己播种 `expandableIds`、搜索后重算(否则命中藏在折叠祖先里看不见)。
 * 将来 pro-components 换版/换库,树页只改这一处(与 `DataTable` 对称)。
 */
export interface TreeTableProps<T extends Record<string, any>> {
  columns: ProColumns<T>[]
  /** 已 `buildTree` 的树(`children` 嵌套);第一列自动承载展开箭头,故树列须排首位。 */
  data: T[]
  loading?: boolean
  rowKey?: string
  /** 受控展开行 key —— 默认全展开由调用方播种 `expandableIds`,`data` 变(搜索/切筛)后须重算。 */
  expandedRowKeys: Key[]
  onExpandedRowKeysChange: (keys: Key[]) => void
  /** 工具栏主操作(搜索框 / 展开折叠 / 新增)。挂左侧 headerTitle,对齐 Vue #toolbar。 */
  toolbar?: ReactNode
  /** 列设置持久化键 → `localStorage['protable:{persistKey}']`;不给则不持久化。 */
  persistKey?: string
}

// 约束用 `Record<string, any>`(同 DataTable):业务实体是 interface,无隐式索引签名,`unknown` 会拒收。
export function TreeTable<T extends Record<string, any>>({
  columns, data, loading, rowKey = 'id', expandedRowKeys, onExpandedRowKeysChange, toolbar, persistKey,
}: TreeTableProps<T>) {
  return (
    <ProTable<T>
      columns={columns}
      dataSource={data}
      loading={loading}
      rowKey={rowKey}
      search={false}
      pagination={false}
      // 无 request → reload/全屏无意义;保留列设置齿轮(density 关掉,树行高本就紧凑)。
      options={{ reload: false, density: false, fullScreen: false }}
      // antd 的 prop 名是 `onExpandedRowsChange`(尽管回调收到的是展开行的 **key** 数组)。
      expandable={{ expandedRowKeys, onExpandedRowsChange: (keys) => onExpandedRowKeysChange(keys as Key[]) }}
      columnsState={persistKey ? { persistenceKey: `protable:${persistKey}`, persistenceType: 'localStorage' } : undefined}
      // 左业务区、右列设置:对齐 Vue #toolbar / DataTable
      headerTitle={toolbar}
    />
  )
}
