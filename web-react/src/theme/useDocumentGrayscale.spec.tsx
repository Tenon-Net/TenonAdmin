import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { renderHook, cleanup } from '@testing-library/react'
import { useDocumentGrayscale } from './useDocumentGrayscale'
import { useAppStore } from '@/stores/app'

beforeEach(() => {
  useAppStore.setState({ grayscale: false })
  document.documentElement.removeAttribute('data-gray')
})
afterEach(() => {
  cleanup()
  document.documentElement.removeAttribute('data-gray')
})

describe('useDocumentGrayscale', () => {
  it('grayscale 初值 false → 无 data-gray;开 → 打上;关 → 撤掉', () => {
    const { rerender } = renderHook(() => useDocumentGrayscale())
    expect(document.documentElement.hasAttribute('data-gray')).toBe(false)

    useAppStore.setState({ grayscale: true })
    rerender()
    expect(document.documentElement.hasAttribute('data-gray')).toBe(true)

    useAppStore.setState({ grayscale: false })
    rerender()
    expect(document.documentElement.hasAttribute('data-gray')).toBe(false)
  })

  it('初值就是 true → 挂载即打上 data-gray(F5 后灰阶偏好立即生效)', () => {
    useAppStore.setState({ grayscale: true })
    renderHook(() => useDocumentGrayscale())
    expect(document.documentElement.hasAttribute('data-gray')).toBe(true)
  })
})
