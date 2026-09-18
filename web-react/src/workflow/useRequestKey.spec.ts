import { describe, expect, it } from 'vitest'
import { ApiError } from '@/api'
import { classifyOutcome, useRequestKey } from './useRequestKey'

describe('workflow/useRequestKey', () => {
  it('value() lazily generates a UUID on first call, and returns the same value on repeated calls before settle', () => {
    const requestKey = useRequestKey()
    const first = requestKey.value()
    expect(first).toBeTruthy()
    expect(requestKey.value()).toBe(first)
  })

  it("settle('network') keeps the current key so the next value() call returns the same UUID", () => {
    const requestKey = useRequestKey()
    const first = requestKey.value()
    requestKey.settle('network')
    expect(requestKey.value()).toBe(first)
  })

  it("settle('success') discards the key so the next value() call returns a new UUID", () => {
    const requestKey = useRequestKey()
    const first = requestKey.value()
    requestKey.settle('success')
    expect(requestKey.value()).not.toBe(first)
  })

  it("settle('error') discards the key the same way as success", () => {
    const requestKey = useRequestKey()
    const first = requestKey.value()
    requestKey.settle('error')
    expect(requestKey.value()).not.toBe(first)
  })

  it('reset() discards the key even without a prior settle', () => {
    const requestKey = useRequestKey()
    const first = requestKey.value()
    requestKey.reset()
    expect(requestKey.value()).not.toBe(first)
  })

  it('classifyOutcome distinguishes ApiError from any other thrown value', () => {
    expect(classifyOutcome(new ApiError(40000))).toBe('error')
    expect(classifyOutcome(new TypeError('network down'))).toBe('network')
  })
})
