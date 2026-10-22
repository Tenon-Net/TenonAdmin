import { describe, expect, it, vi } from 'vitest'
import { createLatestRequest } from './latestRequest'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

describe('createLatestRequest', () => {
  it('快速切换目标时只提交最后一次请求结果', async () => {
    const first = deferred<string>()
    const second = deferred<string>()
    const apply = vi.fn()
    const latest = createLatestRequest()

    const firstRun = latest.run(() => first.promise, apply)
    const secondRun = latest.run(() => second.promise, apply)
    second.resolve('role-b')
    await expect(secondRun).resolves.toBe(true)
    first.resolve('role-a')
    await expect(firstRun).resolves.toBe(false)

    expect(apply).toHaveBeenCalledOnce()
    expect(apply).toHaveBeenCalledWith('role-b')
  })

  it('旧请求迟到失败时不覆盖当前界面的错误状态', async () => {
    const first = deferred<string>()
    const latest = createLatestRequest()

    const firstRun = latest.run(() => first.promise, vi.fn())
    await latest.run(() => Promise.resolve('role-b'), vi.fn())
    first.reject(new Error('stale'))

    await expect(firstRun).resolves.toBe(false)
  })
})
