import { describe, it, expect, afterEach, vi } from 'vitest'
import { render, screen, cleanup, waitFor } from '@testing-library/react'
import type { DictItem } from '@/types/api'

vi.mock('@/api', () => ({ dictApi: { items: vi.fn() } }))
import { dictApi } from '@/api'
import { useDictStore } from '@/stores/dict'
import { DictSelect, toDictOptions } from './DictSelect'

const items: DictItem[] = [
  { label: '男', value: '1', sort: 0, enabled: true },
  { label: '女', value: '2', sort: 1, enabled: true },
  { label: '已停用', value: '9', sort: 2, enabled: false },
]

afterEach(() => {
  cleanup()
  useDictStore.getState().invalidate()
  vi.mocked(dictApi.items).mockReset()
})

describe('toDictOptions(纯逻辑)', () => {
  it('过滤停用项', () => {
    expect(toDictOptions(items).map((o) => o.value)).toEqual(['1', '2'])
  })
  it('映射成 {label,value} 形状', () => {
    expect(toDictOptions([items[0]])).toEqual([{ label: '男', value: '1' }])
  })
  it('空数组 → 空', () => {
    expect(toDictOptions([])).toEqual([])
  })
})

describe('DictSelect 渲染', () => {
  it('placeholder 透传;选项来自字典,停用项不出现', async () => {
    vi.mocked(dictApi.items).mockResolvedValue(items)
    // virtual={false}:关掉 rc-virtual-list,让选项在 happy-dom 里落成真 DOM(否则靠布局测量,测不到)。
    render(<DictSelect typeCode="gender" placeholder="请选择性别" open virtual={false} />)
    expect(screen.getByText('请选择性别')).toBeTruthy() // rest 透传
    await waitFor(() => expect(screen.getByText('男')).toBeTruthy()) // 字典加载后启用项出现
    expect(screen.getByText('女')).toBeTruthy()
    expect(screen.queryByText('已停用')).toBeNull() // 停用项被过滤
  })
})
