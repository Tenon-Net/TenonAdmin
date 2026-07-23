import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { Select, type SelectProps } from 'antd'

export interface ApiSelectOption { label: ReactNode; value: string | number }
export type ApiFetch = (keyword: string) => Promise<ApiSelectOption[] | { items: ApiSelectOption[] }>

/** fetch 结果归一:数组原样;`{items}` 取 items。纯逻辑,变异钉。 */
export function normalizeOptions(res: ApiSelectOption[] | { items: ApiSelectOption[] }): ApiSelectOption[] {
  return Array.isArray(res) ? res : res.items
}

/**
 * 远程选项装载逻辑,**与渲染解耦**(便于 renderHook 确定性地测竞态/防抖/immediate,不趟 antd DOM):
 *   - immediate:挂载即以空关键字预取一页;非 immediate 挂载不发请求
 *   - remote:每次输入(防抖 `debounce`ms)调 fetch;非 remote 交给 Select 客户端过滤,onSearch 空转
 *   - 竞态守卫 `seq`:快速输入 / reloadOn 连发时只认最新一次,旧的在途结果落地即丢弃
 *   - reloadOn 变化(如部门过滤)→ 重新拉;失败留空、不外抛(错误处理留视图层)
 */
export function useRemoteOptions(opts: { fetch: ApiFetch; remote: boolean; immediate: boolean; debounce: number; reloadOn: unknown }) {
  const { remote, immediate, debounce, reloadOn } = opts
  // fetch 存 ref:父常传内联 fetch(每渲染新引用),不能进 effect 依赖,否则 immediate 会反复触发。
  const fetchRef = useRef(opts.fetch)
  fetchRef.current = opts.fetch

  const [options, setOptions] = useState<ApiSelectOption[]>([])
  const [loading, setLoading] = useState(false)
  const seqRef = useRef(0)
  const timerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  const load = useCallback(async (keyword = '') => {
    const id = ++seqRef.current
    setLoading(true)
    try {
      const res = await fetchRef.current(keyword)
      if (id !== seqRef.current) return // 竞态:已有更新的一次在途/落地,丢弃本次结果
      setOptions(normalizeOptions(res))
    } catch {
      if (id === seqRef.current) setOptions([]) // 失败留空(且仅最新一次才清,不误伤后续成功)
    } finally {
      if (id === seqRef.current) setLoading(false)
    }
  }, [])

  // 挂载仅当 immediate 加载一次;此后 reloadOn 每变一次重拉一次(reloadOn 值本身不进 load,只作触发依赖)。
  const mountedRef = useRef(false)
  useEffect(() => {
    if (!mountedRef.current) {
      mountedRef.current = true
      if (immediate) void load('')
      return
    }
    void load('')
  }, [reloadOn, immediate, load])

  useEffect(() => () => clearTimeout(timerRef.current), [])

  const onSearch = useCallback(
    (keyword: string) => {
      if (!remote) return // 非 remote:过滤交给 Select filterOption,不发请求
      clearTimeout(timerRef.current)
      timerRef.current = setTimeout(() => void load(keyword), debounce)
    },
    [remote, debounce, load],
  )

  return { options, loading, onSearch }
}

export interface ApiSelectProps extends Omit<SelectProps, 'options'> {
  fetch: ApiFetch
  /** true 每次输入调 fetch(服务端搜索);false 只加载一次、Select 客户端过滤。默认 true。 */
  remote?: boolean
  /** 挂载即预取一页。默认 true。 */
  immediate?: boolean
  /** 搜索防抖 ms。默认 300。 */
  debounce?: number
  /** 该值变化时重新加载(如 UserSelect 的 orgId 部门过滤)。 */
  reloadOn?: unknown
}

/**
 * 通用远程下拉:父传 `fetch(keyword)` → 选项(已 `{label,value}`)或 `{items}`;本组件管
 * 初次加载 / 远程搜索(防抖)/ loading / 竞态,其余一切经 rest 透传 antd Select。UserSelect 基于此。
 */
export function ApiSelect({ fetch, remote = true, immediate = true, debounce = 300, reloadOn, ...rest }: ApiSelectProps) {
  const { options, loading, onSearch } = useRemoteOptions({ fetch, remote, immediate, debounce, reloadOn })
  return (
    <Select
      allowClear
      {...rest}
      // v6:搜索相关(filterOption/optionFilterProp/onSearch)收进 showSearch 对象;传对象即启用搜索。
      // remote → filterOption:false(服务端过滤,展示 fetch 全量返回);非 remote → 默认按 label 客户端过滤。
      showSearch={{ filterOption: remote ? false : undefined, optionFilterProp: 'label', onSearch }}
      options={options}
      loading={loading}
    />
  )
}
