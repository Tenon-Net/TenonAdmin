import type { SortOrder } from 'antd/es/table/interface'

/** 分页查询入参:页码/页大小 + 排序 + 各页自己的搜索字段(透传)。 */
export interface PageQuery {
  page: number
  pageSize: number
  sortField?: string
  sortOrder?: 'asc' | 'desc'
}
export type PageFetcher<T> = (q: PageQuery & Record<string, unknown>) => Promise<{ items: T[]; total: number }>

/**
 * 把 `(params)=>{items,total}` 的 fetcher 适配成 **antd ProTable 的 `request` 契约** `{data,success,total}`。
 * 隔离 `@ant-design/pro-components`(beta):16 个 CRUD 页只依赖本适配 + `<DataTable>`,不直接碰 ProTable API,
 * 将来 pro-components 换版/换库,改这一处即可。
 *
 * 三处映射:
 *   - 页码:ProTable 的 `current`(从 1 起) → fetcher 的 `page`;`pageSize` 直传。
 *   - 排序:ProTable 的 `{字段: 'ascend'|'descend'}` → `sortField` + `sortOrder`('asc'/'desc')。
 *     **后端 `OrderBySafe` 只认 "desc"(不分大小写),其余按升序** —— 但仍映成 'asc' 而非原样传 'ascend',
 *     免得后端哪天改成严格校验时"排序不生效"(点降序却按默认排)。无排序时两者都留空。
 *   - 搜索:params 里除 current/pageSize 外的字段(搜索表单值)原样 `...` 透传给 fetcher,由各页 api 映 PascalCase。
 */
export function toProTable<T>(fetcher: PageFetcher<T>) {
  // ProTable 的 request 是 `(params, sort, filter)`;本适配**有意只接前两个**:
  // 列级筛选(filter)不支持,筛选一律走搜索表单(16 个页都如此)。多列排序也不接 —— 后端 OrderBySafe
  // 是单字段,`Object.entries(sort)[0]` 只取第一列即够,这是有意的天花板不是遗漏。
  return async (
    params: { current?: number; pageSize?: number } & Record<string, unknown>,
    sort: Record<string, SortOrder>,
  ): Promise<{ data: T[]; total: number; success: boolean }> => {
    const { current, pageSize, ...filters } = params
    const [field, order] = Object.entries(sort ?? {})[0] ?? []
    const { items, total } = await fetcher({
      // `...filters` **摆在最前**:分页/排序是算出来的、必须权威。搜索表单万一有字段恰好叫 `page`/
      // `sortField` 之类(16 个页目前都没有),前置就不会覆盖掉算出的分页值。
      ...filters,
      page: current ?? 1,
      pageSize: pageSize ?? 10,
      // sortField 只在真有排序方向时给 —— order 为空(取消排序)时连字段也不传,回后端默认排序。
      sortField: order ? field : undefined,
      sortOrder: order === 'descend' ? 'desc' : order === 'ascend' ? 'asc' : undefined,
    })
    return { data: items, total, success: true }
  }
}
