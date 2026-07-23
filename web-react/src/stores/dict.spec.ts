import { describe, it, expect, beforeEach, vi } from 'vitest'
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
const OTHER: DictItem[] = [{ label: 'B', value: '2', sort: 0, enabled: true }]

beforeEach(() => {
  useDictStore.getState().invalidate() // 连带清模块级 pending
  // vite.config.ts 的 restoreMocks 只对 vi.spyOn 生效,vi.mock 工厂里的裸 vi.fn() 调用计数不会跨用例自动清零。
  itemsMock.mockClear()
})

describe('useDictStore.load', () => {
  it('缓存命中不重打 API', async () => {
    itemsMock.mockResolvedValue(SAMPLE)
    await useDictStore.getState().load('t1')
    await useDictStore.getState().load('t1')
    expect(itemsMock).toHaveBeenCalledTimes(1)
  })

  it('两次并发 load 同 typeCode 合流成一次请求', async () => {
    itemsMock.mockResolvedValue(SAMPLE)
    const [a, b] = await Promise.all([
      useDictStore.getState().load('t2'),
      useDictStore.getState().load('t2'),
    ])
    expect(a).toEqual(SAMPLE)
    expect(b).toEqual(SAMPLE)
    expect(itemsMock).toHaveBeenCalledTimes(1)
  })

  it('不同 typeCode 各走各的,不会互相合流', async () => {
    itemsMock.mockResolvedValueOnce(SAMPLE).mockResolvedValueOnce(OTHER)
    const [a, b] = await Promise.all([
      useDictStore.getState().load('x'),
      useDictStore.getState().load('y'),
    ])
    expect(a).toEqual(SAMPLE)
    expect(b).toEqual(OTHER)
    expect(itemsMock).toHaveBeenCalledTimes(2)
  })

  it('失败不写缓存,可自然重试成功', async () => {
    itemsMock.mockRejectedValueOnce(new Error('boom')).mockResolvedValueOnce(SAMPLE)
    await expect(useDictStore.getState().load('t3')).rejects.toThrow('boom')
    expect(useDictStore.getState().cache.t3).toBeUndefined()
    await expect(useDictStore.getState().load('t3')).resolves.toEqual(SAMPLE)
    expect(useDictStore.getState().cache.t3).toEqual(SAMPLE)
  })
})

describe('invalidate', () => {
  it('竞态守卫:load 在途中被 invalidate,在途结果不回写', async () => {
    const d = deferred<DictItem[]>()
    itemsMock.mockReturnValue(d.promise)
    const p = useDictStore.getState().load('t4')
    useDictStore.getState().invalidate('t4') // 在请求结果落地前失效
    d.resolve(SAMPLE)
    await p
    expect(useDictStore.getState().cache.t4).toBeUndefined()
  })

  it('连带清 pending:失效后立刻 load,发的是新请求而不是复用在途的旧请求', async () => {
    // 只删 cache 不删 pending 的话,这里第二次 load 会拿到**失效前**那个在途请求的结果,
    // 症状是「字典管理页改完、回列表刷新,看到的还是旧值」,而且只在改动与刷新挨得很近时出现。
    const first = deferred<DictItem[]>()
    const second = deferred<DictItem[]>()
    itemsMock.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)

    const p1 = useDictStore.getState().load('t5')
    useDictStore.getState().invalidate('t5')
    const p2 = useDictStore.getState().load('t5')

    expect(itemsMock).toHaveBeenCalledTimes(2) // 第二次真的另起了请求
    first.resolve(SAMPLE)
    second.resolve(OTHER)
    await Promise.all([p1, p2])
    expect(useDictStore.getState().cache.t5).toEqual(OTHER) // 落地的是新请求的结果
  })

  /**
   * `.finally` 里那半个守卫(`if (pending.get(typeCode) === req)`)保的是**跨 invalidate 边界的去重**,
   * 时序比 `.then` 那半窄得多,所以要单独造:
   *   load → invalidate → 再 load(此时 pending 里是 req2)→ **req1 这时才 settle**。
   * 无条件 `pending.delete` 会把 req2 的登记一起抹掉,于是第三次 load 又另起一个请求。
   * 后果不是脏数据(那归 `.then` 那半守着),而是**去重悄悄失效** —— 一屏 N 个下拉就是 N 次请求。
   *
   * 先前这条守卫**没有任何判据**:把它删成无条件 delete,其余 12 条用例全绿。
   */
  it('finally 守卫:旧请求 settle 不会摘掉新请求的 pending 登记', async () => {
    const first = deferred<DictItem[]>()
    const second = deferred<DictItem[]>()
    itemsMock.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)

    const p1 = useDictStore.getState().load('t6')
    useDictStore.getState().invalidate('t6')
    const p2 = useDictStore.getState().load('t6')
    expect(itemsMock).toHaveBeenCalledTimes(2)

    first.resolve(SAMPLE) // 旧请求现在才落地
    await p1

    const p3 = useDictStore.getState().load('t6') // 应当合流到 req2,而不是另起 req3
    expect(itemsMock).toHaveBeenCalledTimes(2)

    second.resolve(OTHER)
    await Promise.all([p2, p3])
    expect(useDictStore.getState().cache.t6).toEqual(OTHER)
  })

  it('不传 typeCode 清全部', async () => {
    itemsMock.mockResolvedValue(SAMPLE)
    await useDictStore.getState().load('a')
    await useDictStore.getState().load('b')
    expect(Object.keys(useDictStore.getState().cache)).toEqual(['a', 'b'])
    useDictStore.getState().invalidate()
    expect(useDictStore.getState().cache).toEqual({})
  })

  it('只失效指定 typeCode,兄弟键不受影响', async () => {
    itemsMock.mockResolvedValue(SAMPLE)
    await useDictStore.getState().load('keep')
    await useDictStore.getState().load('drop')
    useDictStore.getState().invalidate('drop')
    expect(useDictStore.getState().cache.keep).toEqual(SAMPLE)
    expect(useDictStore.getState().cache.drop).toBeUndefined()
  })
})
