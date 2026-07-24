import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MenuType, type MenuNode } from '@/types/menu'

const mocks = vi.hoisted(() => ({
  auth: { menuTree: [] as MenuNode[], currentModuleId: null as number | null, routesReady: false },
  menu: vi.fn<() => Promise<MenuNode[]>>(),
  addRoute: vi.fn(),
  hasRoute: vi.fn(() => false),
  removeRoute: vi.fn(),
  resetRouter: vi.fn(),
  registerDynamic: vi.fn(),
  registerDetailRoutes: vi.fn(),
  namedPage: vi.fn((_name: string, _loader: () => Promise<unknown>) => ({ name: 'missing-page' })),
}))

vi.mock('@/api', () => ({ personalApi: { menu: mocks.menu } }))
vi.mock('@/stores/auth', () => ({ useAuthStore: () => mocks.auth }))
vi.mock('@/router', () => ({
  router: { addRoute: mocks.addRoute, hasRoute: mocks.hasRoute, removeRoute: mocks.removeRoute },
  resetRouter: mocks.resetRouter,
  registerDynamic: mocks.registerDynamic,
}))
vi.mock('@/router/detailRoutes', () => ({ registerDetailRoutes: mocks.registerDetailRoutes }))
vi.mock('@/router/namedPage', () => ({ namedPage: mocks.namedPage }))

import { buildRoutesForModule } from './useAuthMenu'

function missingMenu(): MenuNode {
  return {
    id: 17,
    parentId: 0,
    type: MenuType.Menu,
    title: 'Broken page',
    path: '/system/broken',
    component: 'system/broken/index',
    icon: 'ph:warning',
    sort: 0,
    visible: true,
    children: [],
  }
}

describe('buildRoutesForModule', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.auth.menuTree = []
    mocks.auth.currentModuleId = null
    mocks.auth.routesReady = false
    mocks.menu.mockResolvedValue([missingMenu()])
  })

  it('缺失组件会告警并把原菜单路径物化成 MissingRoute', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})

    await buildRoutesForModule(9)

    expect(warn).toHaveBeenCalledWith('[menu] 缺少视图组件:', 'system/broken/index')
    expect(mocks.addRoute).toHaveBeenCalledWith('layout', expect.objectContaining({
      path: '/system/broken',
      name: 'menu-17',
      component: { name: 'missing-page' },
      meta: {
        title: 'Broken page',
        icon: 'ph:warning',
        keepAlive: true,
        missingComponent: 'system/broken/index',
      },
    }))
    expect(mocks.registerDynamic).toHaveBeenCalledWith('menu-17')

    const loader = mocks.namedPage.mock.calls[0]?.[1] as (() => Promise<{ default: unknown }>) | undefined
    expect(loader).toBeTypeOf('function')
    expect((await loader!()).default).toBeTruthy()
  })
})
