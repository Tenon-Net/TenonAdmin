import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { MenuType, type MenuNode } from '@/types/menu'

// 切断 auth → tabs → router 链:tabs.ts 顶层 `import { router } from '@/router'`。
vi.mock('@/router', () => ({
  router: {
    hasRoute: vi.fn(() => false),
    removeRoute: vi.fn(),
    push: vi.fn(),
    replace: vi.fn(),
    currentRoute: { value: { path: '/' } },
  },
}))

import { useAuthStore } from './auth'
import { useTabsStore } from './tabs'

beforeEach(() => {
  setActivePinia(createPinia())
})

describe('useAuthStore.homePath', () => {
  it('模块自己的 defaultRoute 优先', () => {
    const auth = useAuthStore()
    auth.modules = [{ id: 1, code: 'x', title: 'X', sort: 0, defaultRoute: '/foo' }]
    auth.currentModuleId = 1
    expect(auth.homePath).toBe('/foo')
  })

  it('无 defaultRoute → 菜单树深度优先第一个叶子', () => {
    const auth = useAuthStore()
    auth.modules = [{ id: 1, code: 'x', title: 'X', sort: 0 }]
    auth.currentModuleId = 1
    // 首根带子目录节点,验证递归会往下找到叶子而非在第一层就短路。
    const tree: MenuNode[] = [
      {
        id: 1, parentId: 0, type: MenuType.Catalog, title: 'dir', sort: 0, visible: true,
        children: [
          {
            id: 2, parentId: 1, type: MenuType.Catalog, title: 'dir2', sort: 0, visible: true,
            children: [
              { id: 3, parentId: 2, type: MenuType.Menu, title: 'leaf', path: '/leaf', sort: 0, visible: true, children: [] },
            ],
          },
        ],
      },
    ]
    auth.menuTree = tree
    expect(auth.homePath).toBe('/leaf')
  })

  it('模块不存在/菜单树空 → /module 兜底', () => {
    const auth = useAuthStore()
    auth.modules = []
    auth.currentModuleId = 1 // 不在 modules 里
    auth.menuTree = []
    expect(auth.homePath).toBe('/module')
  })
})

describe('useAuthStore.hasPerm', () => {
  it('超管恒 true,即使权限码集为空', () => {
    const auth = useAuthStore()
    auth.isSuperAdmin = true
    auth.permissionsLoaded = false
    auth.permissionCodes = []
    expect(auth.hasPerm('GET:/api/v1/anything')).toBe(true)
  })

  it('permissionsLoaded=false 恒 false(fail-closed),即使码在集合里', () => {
    const auth = useAuthStore()
    auth.isSuperAdmin = false
    auth.permissionsLoaded = false
    auth.permissionCodes = ['GET:/api/v1/x']
    expect(auth.hasPerm('GET:/api/v1/x')).toBe(false)
  })

  it('加载完成后精确命中/未命中;reset() 清空字段并连带清空 tabs', () => {
    const auth = useAuthStore()
    auth.isSuperAdmin = false
    auth.permissionsLoaded = true
    auth.permissionCodes = ['GET:/api/v1/x']
    expect(auth.hasPerm('GET:/api/v1/x')).toBe(true)
    expect(auth.hasPerm('GET:/api/v1/y')).toBe(false)

    auth.modules = [{ id: 1, code: 'x', title: 'X', sort: 0 }]
    auth.currentModuleId = 1
    auth.routesReady = true
    const tabs = useTabsStore()
    tabs.tabs = [{ path: '/leaf', fullPath: '/leaf', title: 'leaf', name: 'menu-3' }]

    auth.reset()

    expect(auth.modules).toEqual([])
    expect(auth.currentModuleId).toBeNull()
    expect(auth.defaultModuleId).toBeNull()
    expect(auth.menuTree).toEqual([])
    expect(auth.permissionCodes).toEqual([])
    expect(auth.permissionsLoaded).toBe(false)
    expect(auth.isSuperAdmin).toBe(false)
    expect(auth.routesReady).toBe(false)
    expect(tabs.tabs).toEqual([]) // reset 连带清了 tabs
  })
})
