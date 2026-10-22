import { afterEach, describe, expect, it, vi } from 'vitest'
import { createSerialPoller } from './poller'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => { resolve = res })
  return { promise, resolve }
}

afterEach(() => {
  vi.useRealTimers()
})

describe('createSerialPoller', () => {
  it('请求未结束时不启动下一轮轮询', async () => {
    vi.useFakeTimers()
    const first = deferred<string>()
    const fetch = vi.fn().mockReturnValueOnce(first.promise).mockResolvedValue('second')
    const apply = vi.fn()
    const poller = createSerialPoller({ intervalMs: 15_000, fetch, apply, onError: vi.fn() })

    poller.start()
    await vi.advanceTimersByTimeAsync(30_000)
    expect(fetch).toHaveBeenCalledOnce()
    first.resolve('first')
    await first.promise
    await vi.advanceTimersByTimeAsync(15_000)

    expect(fetch).toHaveBeenCalledTimes(2)
  })

  it('停用后丢弃在途结果，重新启用后串行刷新', async () => {
    const first = deferred<string>()
    const second = deferred<string>()
    const fetch = vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const apply = vi.fn()
    const poller = createSerialPoller({ intervalMs: 15_000, fetch, apply, onError: vi.fn() })

    poller.start()
    poller.stop()
    poller.start()
    expect(fetch).toHaveBeenCalledOnce()
    first.resolve('stale')
    await first.promise
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))
    expect(apply).not.toHaveBeenCalled()
    second.resolve('fresh')
    await second.promise
    await vi.waitFor(() => expect(apply).toHaveBeenCalledWith('fresh'))
    poller.stop()
  })
})
