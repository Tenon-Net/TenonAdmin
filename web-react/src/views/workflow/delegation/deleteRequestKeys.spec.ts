import { describe, expect, it } from 'vitest'
import { createDeleteRequestKeys } from './deleteRequestKeys'

describe('createDeleteRequestKeys', () => {
  it('网络失败保留同键,业务失败换键,不同行互不串键', () => {
    const keys = createDeleteRequestKeys()
    const first = keys.value(1)
    expect(keys.value(2)).not.toBe(first)

    keys.settle(1, 'network')
    expect(keys.value(1)).toBe(first)

    keys.settle(1, 'error')
    expect(keys.value(1)).not.toBe(first)
  })

  it('成功后清除该行请求键', () => {
    const keys = createDeleteRequestKeys()
    const first = keys.value(1)
    keys.settle(1, 'success')
    expect(keys.value(1)).not.toBe(first)
  })
})
