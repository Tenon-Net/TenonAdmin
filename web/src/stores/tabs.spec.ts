import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

// 切断 tabs → router 链(tabs.ts 顶层 import { router })。hasRoute 恒真,让 cachedNames 只由 noCache 决定。
vi.mock('@/router', () => ({
  router: {
    hasRoute: vi.fn(() => true),
    removeRoute: vi.fn(),
    push: vi.fn(),
    replace: vi.fn(),
    currentRoute: { value: { path: '/' } },
  },
}))

import { useTabsStore } from './tabs'

// addTab 只读这几个字段,给个精简的路由替身即可。
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const routeLike = (path: string, name: string, meta: Record<string, unknown> = {}) =>
  ({ path, name, fullPath: path, meta }) as any

beforeEach(() => setActivePinia(createPinia()))

describe('useTabsStore 动态标题 / noCache', () => {
  it('setTitle 置 titleFixed 后,addTab 复访不再用 meta.title 覆盖', () => {
    const tabs = useTabsStore()
    tabs.addTab(routeLike('/system/user/5/detail', 'detail-system-user', { title: 'common.detail' }))
    tabs.setTitle('/system/user/5/detail', '张三')
    expect(tabs.tabs[0]!.title).toBe('张三')
    expect(tabs.tabs[0]!.titleFixed).toBe(true)
    // 复访(afterEach 再次 addTab 同 path):标题保持记录名,不回退到 meta.title
    tabs.addTab(routeLike('/system/user/5/detail', 'detail-system-user', { title: 'common.detail' }))
    expect(tabs.tabs[0]!.title).toBe('张三')
  })

  it('cachedNames 排除 meta.noCache 标签', () => {
    const tabs = useTabsStore()
    tabs.addTab(routeLike('/system/user', 'menu-15', { title: 'user.title' })) // 普通页 → 缓存
    tabs.addTab(routeLike('/system/user/5/detail', 'detail-system-user', { noCache: true })) // 详情 → 不缓存
    expect(tabs.cachedNames).toContain('menu-15')
    expect(tabs.cachedNames).not.toContain('detail-system-user')
  })
})

describe('useTabsStore 固定标签(pin)', () => {
  it('togglePin 后不可关闭,批量关闭时保留,取消后可关闭', () => {
    const tabs = useTabsStore()
    tabs.addTab(routeLike('/a', 'menu-a'))
    tabs.addTab(routeLike('/b', 'menu-b'))
    tabs.addTab(routeLike('/c', 'menu-c'))

    tabs.togglePin('/b')
    expect(tabs.tabs.find((t) => t.path === '/b')!.pinned).toBe(true)

    // 固定标签不可被 removeTab 关闭
    tabs.removeTab('/b')
    expect(tabs.tabs.some((t) => t.path === '/b')).toBe(true)

    // closeOthers('/a') 仍保留固定的 /b
    tabs.closeOthers('/a')
    expect(tabs.tabs.map((t) => t.path).sort()).toEqual(['/a', '/b'])

    // 取消固定后可正常关闭
    tabs.togglePin('/b')
    tabs.removeTab('/b')
    expect(tabs.tabs.some((t) => t.path === '/b')).toBe(false)
  })
})
