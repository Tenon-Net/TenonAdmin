import { describe, it, expect, afterEach, vi } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import type { DictItem } from '@/types/api'

vi.mock('@/api', () => ({ dictApi: { items: vi.fn().mockResolvedValue([]) } }))
import { useDictStore } from '@/stores/dict'
import { DictTag, resolveDictTag } from './DictTag'

const five: DictItem[] = ['a', 'b', 'c', 'd', 'e'].map((v, i) => ({
  label: v.toUpperCase(), value: v, sort: i, enabled: true,
}))

afterEach(() => {
  cleanup()
  useDictStore.getState().invalidate()
})

describe('resolveDictTag(纯逻辑)', () => {
  it('空值 → null(上层渲染「—」)', () => {
    for (const v of [null, undefined, '']) expect(resolveDictTag(five, v)).toBeNull()
  })
  it('命中 → label 取字典文案,color 取 FALLBACK[idx%4]', () => {
    expect(resolveDictTag(five, 'a')).toEqual({ label: 'A', color: 'processing' })
    expect(resolveDictTag(five, 'b')).toEqual({ label: 'B', color: 'success' })
    expect(resolveDictTag(five, 'c')).toEqual({ label: 'C', color: 'warning' })
    expect(resolveDictTag(five, 'd')).toEqual({ label: 'D', color: 'error' })
  })
  it('下标环绕:第 5 项(idx 4)→ FALLBACK[0]=processing', () => {
    expect(resolveDictTag(five, 'e')).toEqual({ label: 'E', color: 'processing' })
  })
  it('停用项不占排序下标(先过滤再取 index)', () => {
    const withDisabled: DictItem[] = [
      { label: 'A', value: 'a', sort: 0, enabled: false },
      { label: 'B', value: 'b', sort: 1, enabled: true },
    ]
    // b 是启用项里第 0 个 → processing(不是把停用的 a 也算进去后的 success)
    expect(resolveDictTag(withDisabled, 'b')).toEqual({ label: 'B', color: 'processing' })
    // a 已停用 → 命不中启用项 → 原始值 + default
    expect(resolveDictTag(withDisabled, 'a')).toEqual({ label: 'a', color: 'default' })
  })
  it('未命中 → label 取原始 value,color 取 default', () => {
    expect(resolveDictTag(five, 'zzz')).toEqual({ label: 'zzz', color: 'default' })
  })
  it('typeMap 显式映射优先于 fallback', () => {
    expect(resolveDictTag(five, 'a', { a: 'warning' })).toEqual({ label: 'A', color: 'warning' })
  })
  it('typeMap 未命中该值 → 回落 fallback', () => {
    expect(resolveDictTag(five, 'a', { b: 'warning' })).toEqual({ label: 'A', color: 'processing' })
  })
})

describe('DictTag 渲染', () => {
  it('空值渲染「—」,不出空标签', () => {
    render(<DictTag typeCode="x" value={null} />)
    expect(screen.getByText('—')).toBeTruthy()
  })
  it('有值:字典未加载完 → 容错渲染原始值', () => {
    render(<DictTag typeCode="x" value="1" />)
    expect(screen.getByText('1')).toBeTruthy()
  })
})
