import { describe, expect, it } from 'vitest'
import { nextTick } from 'vue'
import {
  createPageScrollMemory,
  restorePageScrollBeforePaint,
  shouldPreservePageScroll,
} from './pageScrollMemory'

describe('shouldPreservePageScroll', () => {
  const tabs = [
    { path: '/cached' },
    { path: '/detail', noCache: true },
  ]

  it('只为仍打开的缓存标签保留滚动位置', () => {
    expect(shouldPreservePageScroll(tabs, '/cached')).toBe(true)
    expect(shouldPreservePageScroll(tabs, '/detail')).toBe(false)
    expect(shouldPreservePageScroll(tabs, '/closed')).toBe(false)
  })
})

describe('createPageScrollMemory', () => {
  it('恢复缓存位置，刷新重置并清理已关闭标签', () => {
    const memory = createPageScrollMemory()
    memory.save('/a', 120)
    memory.save('/b', 240)
    expect(memory.read('/a')).toBe(120)

    memory.reset('/a')
    expect(memory.read('/a')).toBe(0)

    memory.prune(['/a'])
    expect(memory.read('/b')).toBe(0)
  })

  it('noCache 页面既不保存也不恢复，并清理同路径遗留位置', () => {
    const memory = createPageScrollMemory()
    memory.save('/detail', 160, true)

    expect(memory.read('/detail', false)).toBe(0)
    memory.save('/detail', 240, false)
    expect(memory.read('/detail', true)).toBe(0)
  })
})

describe('restorePageScrollBeforePaint', () => {
  it('等待新页面 DOM 提交后再恢复，避免空内容把 scrollTop 截断为 0', async () => {
    let restored = false
    const pending = restorePageScrollBeforePaint(() => {
      restored = true
    })
    expect(restored).toBe(false)

    await nextTick()
    await pending
    expect(restored).toBe(true)
  })
})
