import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import type { DictItem } from '@/types/api'

vi.mock('@/api', () => ({ dictApi: { items: vi.fn() } }))

import { dictApi } from '@/api'
import { useDictStore } from './dict'

const itemsMock = vi.mocked(dictApi.items)

function deferred<T>() {
  let resolve!: (v: T) => void
  let reject!: (e: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const SAMPLE: DictItem[] = [{ label: 'A', value: '1', sort: 0, enabled: true }]

beforeEach(() => {
  setActivePinia(createPinia())
  useDictStore().invalidate() // 清模块级 pending(不随 pinia 实例重建而清)
  // vite.config.ts 的 restoreMocks 只对 vi.spyOn 生效,vi.mock 工厂里的裸 vi.fn() 调用计数不会跨用例自动清零。
  itemsMock.mockClear()
})

describe('useDictStore.load', () => {
  it('缓存命中不重打 API', async () => {
    itemsMock.mockResolvedValue(SAMPLE)
    const store = useDictStore()
    await store.load('t1')
    await store.load('t1')
    expect(itemsMock).toHaveBeenCalledTimes(1)
  })

  it('两次并发 load 同 typeCode 合流成一次请求', async () => {
    itemsMock.mockResolvedValue(SAMPLE)
    const store = useDictStore()
    const [a, b] = await Promise.all([store.load('t2'), store.load('t2')])
    expect(a).toEqual(SAMPLE)
    expect(b).toEqual(SAMPLE)
    expect(itemsMock).toHaveBeenCalledTimes(1)
  })

  it('失败不写缓存,可自然重试成功', async () => {
    itemsMock.mockRejectedValueOnce(new Error('boom')).mockResolvedValueOnce(SAMPLE)
    const store = useDictStore()
    await expect(store.load('t3')).rejects.toThrow('boom')
    expect(store.cache.t3).toBeUndefined()
    await expect(store.load('t3')).resolves.toEqual(SAMPLE)
    expect(store.cache.t3).toEqual(SAMPLE)
  })

  it('invalidate 竞态守卫:load 在途中被 invalidate,在途结果不回写', async () => {
    const d = deferred<DictItem[]>()
    itemsMock.mockReturnValue(d.promise)
    const store = useDictStore()
    const p = store.load('t4')
    store.invalidate('t4') // 在请求结果落地前失效
    d.resolve(SAMPLE)
    await p
    expect(store.cache.t4).toBeUndefined()
  })
})
