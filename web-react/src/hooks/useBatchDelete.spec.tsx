import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { renderHook, act, cleanup } from '@testing-library/react'
import '@/locales'

// mock useConfirm:捕获 confirm 调用,并按需执行其 action(以观测 remove 的入参)。
const { confirmMock } = vi.hoisted(() => ({ confirmMock: vi.fn() }))
vi.mock('./useConfirm', () => ({ useConfirm: () => ({ confirm: confirmMock }) }))

import { useBatchDelete } from './useBatchDelete'

afterEach(cleanup)

let confirmResult = true
beforeEach(() => {
  confirmResult = true
  // 默认实现:执行 action(触发 remove),返回受控结果。confirmResult 在调用时读,测试可先改。
  confirmMock.mockReset()
  confirmMock.mockImplementation(async (o: { action: () => Promise<unknown> }) => {
    await o.action()
    return confirmResult
  })
})

function setup() {
  const remove = vi.fn().mockResolvedValue(true)
  const refresh = vi.fn()
  const { result } = renderHook(() => useBatchDelete({ remove, refresh, successMsg: 'ok' }))
  return { remove, refresh, result }
}

describe('useBatchDelete', () => {
  it('空选:run 不弹确认、不删不刷新', async () => {
    const { remove, refresh, result } = setup()
    await act(async () => { await result.current.run() })
    expect(confirmMock).not.toHaveBeenCalled()
    expect(remove).not.toHaveBeenCalled()
    expect(refresh).not.toHaveBeenCalled()
  })

  it('有选 + 确认成功:主键 map(Number) 传 remove、清选 + 刷新', async () => {
    const { remove, refresh, result } = setup()
    act(() => { result.current.setSelectedKeys(['1', '2', '3']) }) // ProTable 可能给字符串键
    expect(result.current.hasSelection).toBe(true)
    await act(async () => { await result.current.run() })
    expect(remove).toHaveBeenCalledWith([1, 2, 3]) // 字符串键 → number[](钉住 map(Number))
    expect(confirmMock).toHaveBeenCalledWith(expect.objectContaining({ successMsg: 'ok' }))
    expect(result.current.selectedKeys).toEqual([]) // 成功清选
    expect(refresh).toHaveBeenCalledTimes(1) // 成功刷新
  })

  it('确认失败/取消:保留勾选、不刷新', async () => {
    confirmResult = false
    const { refresh, result } = setup()
    act(() => { result.current.setSelectedKeys(['5', '6']) })
    await act(async () => { await result.current.run() })
    expect(result.current.selectedKeys).toEqual(['5', '6']) // 保留,可重试
    expect(refresh).not.toHaveBeenCalled()
  })
})
