import { describe, it, expect, beforeEach, vi } from 'vitest'
import { render, screen, waitFor, cleanup } from '@testing-library/react'
import { useState } from 'react'
import type { DictItem } from '@/types/api'

vi.mock('@/api', () => ({ dictApi: { items: vi.fn() } }))

import { dictApi } from '@/api'
import { useDictStore, useDictOptions } from './dict'

const itemsMock = vi.mocked(dictApi.items)
const GENDER: DictItem[] = [{ label: '男', value: '1', sort: 0, enabled: true }]
const STATUS: DictItem[] = [{ label: '启用', value: 'on', sort: 0, enabled: true }]

beforeEach(() => {
  // `globals: false`,所以 testing-library 的自动 cleanup 不会被注册(它挂在全局 afterEach 上)。
  // 不清的话上一个用例的 DOM 留在 document 里,`getByTestId` 报 "Found multiple elements"。
  cleanup()
  useDictStore.getState().invalidate()
  itemsMock.mockClear()
})

let renders = 0
function Options({ code }: { code: string }) {
  renders++
  const items = useDictOptions(code)
  return <div data-testid="out">{items.map((i) => i.label).join(',') || '(空)'}</div>
}

describe('useDictOptions', () => {
  beforeEach(() => {
    renders = 0
  })

  it('挂载即触发加载,拿到后渲染出来', async () => {
    itemsMock.mockResolvedValue(GENDER)
    render(<Options code="gender" />)
    expect(screen.getByTestId('out').textContent).toBe('(空)') // 首帧还没数据
    await waitFor(() => expect(screen.getByTestId('out').textContent).toBe('男'))
    expect(itemsMock).toHaveBeenCalledWith('gender')
  })

  /**
   * **兜底必须在选择器之外。** zustand 每次渲染都跑选择器并与上次结果 `Object.is` 比对,
   * `s.cache[code] ?? []` 写在**选择器里**时每次都是新数组 = 每次都"变了" = 无限重渲染
   * (React 抛 "The result of getSnapshot should be cached to avoid an infinite loop")。
   *
   * 变异实测把话说准了:把兜底挪进选择器 → 本文件四条**全红**;
   * 只把 `?? EMPTY` 换成 `?? []`(兜底仍在外面)→ **全绿**。
   * 所以起作用的是**位置**,不是"用不用常量" —— `dict.ts` 里 `EMPTY` 的注释按这个结论改过了。
   *
   * 判据的形状也要注意:这里断的是**渲染次数有上界**,不是"EMPTY 是同一个引用"。
   * 后者是回声(常量当然等于自己);前者才是那个 bug 真正的样子。
   */
  it('缓存里没有这个 typeCode 时不会无限重渲染', async () => {
    itemsMock.mockResolvedValue([])
    render(<Options code="never-there" />)
    await waitFor(() => expect(itemsMock).toHaveBeenCalled())
    await new Promise((r) => setTimeout(r, 50))
    // StrictMode 双渲染 + 数据落地一次重渲染,个位数足矣;真无限循环时这里是成千上万。
    expect(renders).toBeLessThan(10)
  })

  it('typeCode 变化会重新加载(级联场景)', async () => {
    itemsMock.mockImplementation((code: string) => Promise.resolve(code === 'gender' ? GENDER : STATUS))

    function Switcher() {
      const [code, setCode] = useState('gender')
      return (
        <>
          <button onClick={() => setCode('status')}>切</button>
          <Options code={code} />
        </>
      )
    }
    render(<Switcher />)
    await waitFor(() => expect(screen.getByTestId('out').textContent).toBe('男'))

    screen.getByText('切').click()
    await waitFor(() => expect(screen.getByTestId('out').textContent).toBe('启用'))
    expect(itemsMock).toHaveBeenCalledWith('status')
  })

  it('加载失败静默:不抛到组件外,渲染成空', async () => {
    itemsMock.mockRejectedValue(new Error('boom'))
    render(<Options code="broken" />)
    await waitFor(() => expect(itemsMock).toHaveBeenCalled())
    await new Promise((r) => setTimeout(r, 20))
    expect(screen.getByTestId('out').textContent).toBe('(空)')
  })
})
