import { describe, it, expect, beforeEach } from 'vitest'
import { MenuType, type MenuNode } from '@/types/menu'
import { useAuthStore, homePath, hasPerm } from './auth'

beforeEach(() => {
  useAuthStore.getState().reset()
  localStorage.clear()
})

describe('homePath', () => {
  it('模块自己的 defaultRoute 优先', () => {
    useAuthStore.setState({
      modules: [{ id: 1, code: 'x', title: 'X', sort: 0, defaultRoute: '/foo' }],
      currentModuleId: 1,
    })
    expect(homePath(useAuthStore.getState())).toBe('/foo')
  })

  it('无 defaultRoute → 菜单树深度优先第一个叶子', () => {
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
    useAuthStore.setState({ modules: [{ id: 1, code: 'x', title: 'X', sort: 0 }], currentModuleId: 1, menuTree: tree })
    expect(homePath(useAuthStore.getState())).toBe('/leaf')
  })

  it('模块不存在/菜单树空 → /module 兜底', () => {
    useAuthStore.setState({ modules: [], currentModuleId: 1, menuTree: [] })
    expect(homePath(useAuthStore.getState())).toBe('/module')
  })
})

describe('hasPerm', () => {
  it('超管恒 true,即使权限码集为空', () => {
    expect(hasPerm({ isSuperAdmin: true, permissionsLoaded: false, permissionCodes: [] }, 'GET:/api/v1/anything')).toBe(true)
  })

  it('permissionsLoaded=false 恒 false(fail-closed),即使码在集合里', () => {
    expect(hasPerm({ isSuperAdmin: false, permissionsLoaded: false, permissionCodes: ['GET:/api/v1/x'] }, 'GET:/api/v1/x')).toBe(false)
  })

  it('加载完成后精确命中/未命中', () => {
    const s = { isSuperAdmin: false, permissionsLoaded: true, permissionCodes: ['GET:/api/v1/x'] }
    expect(hasPerm(s, 'GET:/api/v1/x')).toBe(true)
    expect(hasPerm(s, 'GET:/api/v1/y')).toBe(false)
  })
})

describe('reset', () => {
  it('清空全部授权字段', () => {
    useAuthStore.setState({
      modules: [{ id: 1, code: 'x', title: 'X', sort: 0 }],
      currentModuleId: 1,
      defaultModuleId: 2,
      menuTree: [{ id: 1, parentId: 0, type: MenuType.Menu, title: 'a', path: '/a', sort: 0, visible: true, children: [] }],
      permissionCodes: ['GET:/api/v1/x'],
      permissionsLoaded: true,
      isSuperAdmin: true,
      routesReady: true,
    })

    useAuthStore.getState().reset()

    const s = useAuthStore.getState()
    expect(s.modules).toEqual([])
    expect(s.currentModuleId).toBeNull()
    expect(s.defaultModuleId).toBeNull()
    expect(s.menuTree).toEqual([])
    expect(s.permissionCodes).toEqual([])
    expect(s.permissionsLoaded).toBe(false)
    expect(s.isSuperAdmin).toBe(false)
    expect(s.routesReady).toBe(false)
  })
})

// 这一组 Vue 侧没有对应用例,是移植时补的:持久化白名单错了**不会有任何症状**,直到用户 F5 ——
// 而 `routesReady: true` 一旦落盘,重建被跳过,每条动态路由 404。lint/tsc/build 全绿。
describe('持久化白名单', () => {
  it('只落 currentModuleId,菜单树/权限/routesReady 一概不落盘', () => {
    useAuthStore.setState({
      currentModuleId: 7,
      routesReady: true,
      permissionsLoaded: true,
      permissionCodes: ['GET:/api/v1/x'],
      menuTree: [{ id: 1, parentId: 0, type: MenuType.Menu, title: 'a', path: '/a', sort: 0, visible: true, children: [] }],
      modules: [{ id: 1, code: 'x', title: 'X', sort: 0 }],
      isSuperAdmin: true,
    })

    const saved = JSON.parse(localStorage.getItem('auth')!).state
    expect(Object.keys(saved)).toEqual(['currentModuleId'])
    expect(saved.currentModuleId).toBe(7)
  })

  it('reset 的净态是新对象:就地改过数组之后再 reset 仍然干净', () => {
    // `EMPTY` 写成工厂而不是常量的理由,代码注释里写了但没有用例守着(变异证实:改成常量一条都不红)。
    // 常量的话三个数组在初始态与每次 reset 之间是**同一个实例**,任何一处就地改动都会永久污染净态。
    useAuthStore.getState().reset()
    const codes = useAuthStore.getState().permissionCodes
    codes.push('被污染的码') // 就地改,模拟别处一句 sort()/push()
    useAuthStore.getState().reset()
    expect(useAuthStore.getState().permissionCodes).toEqual([])
    expect(useAuthStore.getState().menuTree).toEqual([])
    expect(useAuthStore.getState().modules).toEqual([])
  })
})
