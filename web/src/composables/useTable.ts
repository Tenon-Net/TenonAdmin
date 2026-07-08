import { reactive, ref, type Ref } from 'vue'

export interface PageResult<T> {
  items: T[]
  total: number
}

export type TableFetcher<T> = (
  params: { page: number; pageSize: number } & Record<string, unknown>,
) => Promise<PageResult<T>>

/**
 * 列表页逻辑单源(与 Naive 无关):loading / 数据 / 查询参数 / 分页 / 加载。
 * 视图提供 columns 与 onError(把 Naive 消息留在视图层,不进 composable,设计 §7.2)。
 */
export function useTable<T>(
  fetcher: TableFetcher<T>,
  opts?: { initParams?: Record<string, unknown>; onError?: (e: unknown) => void; immediate?: boolean },
) {
  const loading = ref(false)
  const rows = ref<T[]>([]) as Ref<T[]>
  const params = reactive<Record<string, unknown>>({ ...(opts?.initParams ?? {}) })
  const pagination = reactive({ page: 1, pageSize: 10, itemCount: 0 })

  async function load() {
    loading.value = true
    try {
      const { items, total } = await fetcher({ page: pagination.page, pageSize: pagination.pageSize, ...params })
      rows.value = items
      pagination.itemCount = total
    } catch (e) {
      opts?.onError?.(e)
    } finally {
      loading.value = false
    }
  }

  function search() {
    pagination.page = 1
    return load()
  }
  function onPage(p: number) {
    pagination.page = p
    return load()
  }
  function onPageSize(s: number) {
    pagination.pageSize = s
    pagination.page = 1
    return load()
  }

  if (opts?.immediate !== false) void load()

  return { loading, rows, params, pagination, load, search, onPage, onPageSize }
}
