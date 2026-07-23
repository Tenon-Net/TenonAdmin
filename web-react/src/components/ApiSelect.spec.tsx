import { describe, it, expect, afterEach, vi } from 'vitest'
import { renderHook, render, screen, act, waitFor, cleanup } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import { ApiSelect, useRemoteOptions, normalizeOptions, type ApiSelectOption } from './ApiSelect'

afterEach(cleanup)

describe('normalizeOptions(纯逻辑)', () => {
  it('数组原样', () => {
    expect(normalizeOptions([{ label: 'a', value: 1 }])).toEqual([{ label: 'a', value: 1 }])
  })
  it('{items} 取 items', () => {
    expect(normalizeOptions({ items: [{ label: 'b', value: 2 }] })).toEqual([{ label: 'b', value: 2 }])
  })
})

describe('useRemoteOptions', () => {
  it('immediate=true:挂载即以空关键字加载一次', async () => {
    const fetch = vi.fn(async (_k: string) => [] as ApiSelectOption[])
    renderHook(() => useRemoteOptions({ fetch, remote: true, immediate: true, debounce: 0, reloadOn: undefined }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(''))
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('immediate=false:挂载不加载', () => {
    const fetch = vi.fn(async (_k: string) => [] as ApiSelectOption[])
    renderHook(() => useRemoteOptions({ fetch, remote: true, immediate: false, debounce: 0, reloadOn: undefined }))
    expect(fetch).not.toHaveBeenCalled()
  })

  it('竞态守卫:旧的在途结果晚到,也不覆盖新结果', async () => {
    const deferreds: Array<(v: ApiSelectOption[]) => void> = []
    const fetch = vi.fn((_k: string) => new Promise<ApiSelectOption[]>((resolve) => deferreds.push(resolve)))
    const { result, rerender } = renderHook(
      ({ reloadOn }) => useRemoteOptions({ fetch, remote: true, immediate: true, debounce: 0, reloadOn }),
      { initialProps: { reloadOn: 0 } },
    )
    // 挂载 → load#1(deferreds[0]);reloadOn 变 → load#2(deferreds[1])
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1))
    rerender({ reloadOn: 1 })
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))
    // 先落地最新一次(#2)
    await act(async () => { deferreds[1]([{ label: 'B', value: 2 }]) })
    expect(result.current.options).toEqual([{ label: 'B', value: 2 }])
    // 旧的一次(#1)晚到:被 seq 守卫丢弃,不回写
    await act(async () => { deferreds[0]([{ label: 'A', value: 1 }]) })
    expect(result.current.options).toEqual([{ label: 'B', value: 2 }])
  })

  it('远程搜索防抖:连打只发最后一次', () => {
    vi.useFakeTimers()
    try {
      const fetch = vi.fn(async (_k: string) => [] as ApiSelectOption[])
      const { result } = renderHook(() => useRemoteOptions({ fetch, remote: true, immediate: false, debounce: 300, reloadOn: undefined }))
      act(() => { result.current.onSearch('a'); result.current.onSearch('ab'); result.current.onSearch('abc') })
      expect(fetch).not.toHaveBeenCalled() // 防抖窗口内不发
      act(() => { vi.advanceTimersByTime(300) })
      expect(fetch).toHaveBeenCalledTimes(1)
      expect(fetch).toHaveBeenCalledWith('abc')
    } finally {
      vi.useRealTimers()
    }
  })

  it('非 remote:onSearch 不发请求(交给客户端过滤)', () => {
    vi.useFakeTimers()
    try {
      const fetch = vi.fn(async (_k: string) => [] as ApiSelectOption[])
      const { result } = renderHook(() => useRemoteOptions({ fetch, remote: false, immediate: false, debounce: 0, reloadOn: undefined }))
      act(() => result.current.onSearch('x'))
      act(() => { vi.advanceTimersByTime(10) })
      expect(fetch).not.toHaveBeenCalled()
    } finally {
      vi.useRealTimers()
    }
  })

  it('先成功后失败:失败把 options 归空(catch 分支,不留陈旧项)', async () => {
    const fetch = vi.fn(async (_k: string) => [] as ApiSelectOption[])
    fetch.mockResolvedValueOnce([{ label: 'A', value: 1 }]).mockRejectedValueOnce(new Error('x'))
    const { result, rerender } = renderHook(
      ({ reloadOn }) => useRemoteOptions({ fetch, remote: true, immediate: true, debounce: 0, reloadOn }),
      { initialProps: { reloadOn: 0 } },
    )
    await waitFor(() => expect(result.current.options).toEqual([{ label: 'A', value: 1 }]))
    rerender({ reloadOn: 1 }) // 触发失败的那次
    await waitFor(() => expect(result.current.options).toEqual([]))
  })
})

describe('ApiSelect 渲染', () => {
  it('渲染 combobox、rest 透传、挂载即预取', async () => {
    const fetch = vi.fn(async (_k: string) => [{ label: '甲', value: 1 }] as ApiSelectOption[])
    render(
      <AntdApp>
        <ApiSelect fetch={fetch} placeholder="搜一下" />
      </AntdApp>,
    )
    expect(screen.getByText('搜一下')).toBeTruthy() // rest 透传
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('')) // immediate 默认 true
  })
})
