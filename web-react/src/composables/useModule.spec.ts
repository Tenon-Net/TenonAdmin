import { describe, it, expect, beforeEach, vi } from 'vitest'
import type { AppModule, MenuNode } from '@/types/menu'
import type { UserProfile } from '@/types/api'

vi.mock('@/api', () => ({
  personalApi: { modules: vi.fn(), menu: vi.fn(), permissions: vi.fn(), profile: vi.fn(), setDefaultModule: vi.fn() },
}))

import { personalApi } from '@/api'
import { enterInitial, enter, switchModule, setDefault } from './useModule'
import { useAuthStore } from '@/stores/auth'
import { useUserStore } from '@/stores/user'

const modulesMock = vi.mocked(personalApi.modules)
const menuMock = vi.mocked(personalApi.menu)
const permsMock = vi.mocked(personalApi.permissions)
const profileMock = vi.mocked(personalApi.profile)
const setDefaultMock = vi.mocked(personalApi.setDefaultModule)

const mod = (id: number): AppModule => ({ id, code: `m${id}`, title: `M${id}`, sort: 0 })
const PROFILE: UserProfile = { id: 1, account: 'a', name: 'n', isSuperAdmin: false, avatar: null }

beforeEach(() => {
  vi.clearAllMocks()
  useAuthStore.getState().reset()
  useUserStore.setState({ accessToken: 't', refreshToken: 'r', userInfo: { userId: 1, account: 'a', name: 'n', mustChangePassword: false } })
  // 默认:权限码与 profile 都成功,menu 返回空树(具体用例覆盖)。
  permsMock.mockResolvedValue(['GET:/x'])
  profileMock.mockResolvedValue(PROFILE)
  menuMock.mockResolvedValue([])
})

describe('enterInitial 决策阶梯', () => {
  it('0 模块 → chooser', async () => {
    modulesMock.mockResolvedValue({ modules: [], defaultModuleId: null })
    expect(await enterInitial()).toEqual({ chooser: true })
    expect(menuMock).not.toHaveBeenCalled() // 没进任何应用
  })

  it('记住的应用仍有效 → 直接进它(哪怕有默认、有多个)', async () => {
    useAuthStore.setState({ currentModuleId: 7 })
    modulesMock.mockResolvedValue({ modules: [mod(3), mod(7), mod(9)], defaultModuleId: 3 })
    expect(await enterInitial()).toEqual({ chooser: false, moduleId: 7 })
    expect(menuMock).toHaveBeenCalledWith(7) // 进的是记住的 7,不是默认 3
  })

  it('记住的应用已不在列表 → 回退阶梯,不进它', async () => {
    useAuthStore.setState({ currentModuleId: 99 }) // 已被收回
    modulesMock.mockResolvedValue({ modules: [mod(3), mod(9)], defaultModuleId: 3 })
    const r = await enterInitial()
    expect(r).toEqual({ chooser: false, moduleId: 3 }) // 落到默认,而不是进不存在的 99
    expect(menuMock).not.toHaveBeenCalledWith(99)
  })

  it('单个应用 → 直接进(无记住、无默认)', async () => {
    modulesMock.mockResolvedValue({ modules: [mod(5)], defaultModuleId: null })
    expect(await enterInitial()).toEqual({ chooser: false, moduleId: 5 })
  })

  it('有默认且有效 → 进默认(无记住、多个应用)', async () => {
    modulesMock.mockResolvedValue({ modules: [mod(3), mod(8)], defaultModuleId: 8 })
    expect(await enterInitial()).toEqual({ chooser: false, moduleId: 8 })
  })

  it('记住优先于默认:两者都有效且不同 → 进记住的', async () => {
    // 这条钉的是**阶梯顺序**。remembered=8、default=3,两者都在列表、都有效。
    // 换成"default 先判"的话会进 3,这条就红。
    useAuthStore.setState({ currentModuleId: 8 })
    modulesMock.mockResolvedValue({ modules: [mod(3), mod(8)], defaultModuleId: 3 })
    expect(await enterInitial()).toEqual({ chooser: false, moduleId: 8 })
  })

  it('多应用、无记住、无默认 → chooser', async () => {
    modulesMock.mockResolvedValue({ modules: [mod(3), mod(8)], defaultModuleId: null })
    expect(await enterInitial()).toEqual({ chooser: true })
  })

  it('多应用、默认指向一个已不在列表的应用 → chooser(不进那个默认)', async () => {
    modulesMock.mockResolvedValue({ modules: [mod(3), mod(8)], defaultModuleId: 99 })
    expect(await enterInitial()).toEqual({ chooser: true })
  })
})

describe('enterInitial 副作用:权限/超管/头像', () => {
  it('权限码成功 → permissionsLoaded=true,码写进 store', async () => {
    modulesMock.mockResolvedValue({ modules: [], defaultModuleId: null })
    permsMock.mockResolvedValue(['GET:/a', 'POST:/b'])
    await enterInitial()
    expect(useAuthStore.getState().permissionsLoaded).toBe(true)
    expect(useAuthStore.getState().permissionCodes).toEqual(['GET:/a', 'POST:/b'])
  })

  it('权限码拉取失败 → permissionsLoaded=false(fail-closed,不谎报有权限)', async () => {
    modulesMock.mockResolvedValue({ modules: [], defaultModuleId: null })
    permsMock.mockRejectedValue(new Error('boom'))
    await enterInitial()
    expect(useAuthStore.getState().permissionsLoaded).toBe(false)
    expect(useAuthStore.getState().permissionCodes).toEqual([])
  })

  it('profile 成功 → isSuperAdmin 跟随,头像回填 user store', async () => {
    modulesMock.mockResolvedValue({ modules: [], defaultModuleId: null })
    profileMock.mockResolvedValue({ ...PROFILE, isSuperAdmin: true, avatar: '/a.png' })
    await enterInitial()
    expect(useAuthStore.getState().isSuperAdmin).toBe(true)
    expect(useUserStore.getState().userInfo?.avatar).toBe('/a.png')
  })

  it('profile 拉取失败 → isSuperAdmin=false(安全侧,不误放行超管)', async () => {
    modulesMock.mockResolvedValue({ modules: [], defaultModuleId: null })
    profileMock.mockRejectedValue(new Error('boom'))
    await enterInitial()
    expect(useAuthStore.getState().isSuperAdmin).toBe(false)
  })
})

describe('enter', () => {
  it('拉菜单 → menuTree/currentModuleId/routesReady 落地', async () => {
    menuMock.mockResolvedValue([{ id: 1, parentId: 0, type: 2, title: 't', path: 'x', sort: 0, visible: true, children: [] }] as never)
    const r = await enter(4)
    expect(r).toEqual({ chooser: false, moduleId: 4 })
    const s = useAuthStore.getState()
    expect(s.currentModuleId).toBe(4)
    expect(s.routesReady).toBe(true)
    expect(s.menuTree).toHaveLength(1)
  })
})

describe('switchModule', () => {
  const LEAF: MenuNode[] = [{ id: 1, parentId: 0, type: 2, title: '页', path: '/leaf', sort: 0, visible: true, children: [] }]

  it('进目标应用并返回它的首页(模块自带 defaultRoute 优先)', async () => {
    // homePath 要在 modules 里找到该模块读 defaultRoute,所以先把 modules 放进 store。
    useAuthStore.setState({ modules: [{ id: 5, code: 'm5', title: 'M5', sort: 0, defaultRoute: '/dash' }] })
    menuMock.mockResolvedValue(LEAF)
    const home = await switchModule(5)
    expect(home).toBe('/dash') // 模块的 defaultRoute 优先于菜单首叶
    expect(useAuthStore.getState().currentModuleId).toBe(5)
    expect(useAuthStore.getState().routesReady).toBe(true)
  })

  it('模块无 defaultRoute → 回落菜单树第一个叶子', async () => {
    useAuthStore.setState({ modules: [{ id: 5, code: 'm5', title: 'M5', sort: 0 }] })
    menuMock.mockResolvedValue(LEAF)
    expect(await switchModule(5)).toBe('/leaf')
  })
})

describe('setDefault', () => {
  it('调后端设默认 + 本地同步 defaultModuleId(角标立刻转移,不重拉)', async () => {
    setDefaultMock.mockResolvedValue(true)
    useAuthStore.setState({ defaultModuleId: 1 })
    await setDefault(7)
    expect(setDefaultMock).toHaveBeenCalledWith(7)
    expect(useAuthStore.getState().defaultModuleId).toBe(7)
  })

  it('后端失败则抛出(页面 catch 提示),本地不改', async () => {
    setDefaultMock.mockRejectedValue(new Error('boom'))
    useAuthStore.setState({ defaultModuleId: 1 })
    await expect(setDefault(7)).rejects.toThrow('boom')
    expect(useAuthStore.getState().defaultModuleId).toBe(1) // 没被改
  })
})
