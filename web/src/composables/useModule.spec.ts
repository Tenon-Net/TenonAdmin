import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import type { UserProfile } from '@/types/api'
import type { AppModule } from '@/types/menu'

vi.mock('@/api', () => ({
  personalApi: {
    modules: vi.fn(),
    permissions: vi.fn(),
    profile: vi.fn(),
    setDefaultModule: vi.fn(),
  },
}))
vi.mock('./useAuthMenu', () => ({ buildRoutesForModule: vi.fn() }))
vi.mock('@/router', () => ({
  router: { hasRoute: vi.fn(() => false), removeRoute: vi.fn(), push: vi.fn(), replace: vi.fn(), currentRoute: { value: { path: '/' } } },
}))

import { personalApi } from '@/api'
import { buildRoutesForModule } from './useAuthMenu'
import { useModule } from './useModule'
import { useAuthStore } from '@/stores/auth'
import { useUserStore } from '@/stores/user'

const modulesMock = vi.mocked(personalApi.modules)
const permissionsMock = vi.mocked(personalApi.permissions)
const profileMock = vi.mocked(personalApi.profile)
const buildRoutesMock = vi.mocked(buildRoutesForModule)

function mod(id: number, defaultRoute?: string): AppModule {
  return { id, code: `m${id}`, title: `M${id}`, sort: 0, defaultRoute }
}
function profile(overrides: Partial<UserProfile> = {}): UserProfile {
  return { id: 1, account: 'a', name: 'A', isSuperAdmin: false, avatar: null, ...overrides }
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  permissionsMock.mockResolvedValue(['GET:/x'])
  profileMock.mockResolvedValue(profile())
})

describe('useModule().enterInitial', () => {
  it('modules 空 → {chooser:true},权限码/超管/默认模块落 auth store,不因 userInfo=null 崩溃', async () => {
    modulesMock.mockResolvedValue({ modules: [], defaultModuleId: null })
    profileMock.mockResolvedValue(profile({ isSuperAdmin: true }))

    const auth = useAuthStore()
    const res = await useModule().enterInitial()

    expect(res).toEqual({ chooser: true })
    expect(auth.permissionCodes).toEqual(['GET:/x'])
    expect(auth.permissionsLoaded).toBe(true)
    expect(auth.isSuperAdmin).toBe(true)
    expect(auth.defaultModuleId).toBeNull()
    expect(useUserStore().userInfo).toBeNull() // 未登录场景,回填头像分支被跳过,不炸
  })

  it('持久化的 currentModuleId 命中 → enter(remembered) 优先于 defaultModuleId', async () => {
    modulesMock.mockResolvedValue({ modules: [mod(1), mod(2)], defaultModuleId: 2 })
    const auth = useAuthStore()
    auth.currentModuleId = 1 // 模拟 F5 后持久化字段已恢复

    const res = await useModule().enterInitial()

    expect(buildRoutesMock).toHaveBeenCalledWith(1)
    expect(res).toEqual({ chooser: false, moduleId: 1 })
  })

  it('单模块直进', async () => {
    modulesMock.mockResolvedValue({ modules: [mod(7)], defaultModuleId: null })

    const res = await useModule().enterInitial()

    expect(buildRoutesMock).toHaveBeenCalledWith(7)
    expect(res).toEqual({ chooser: false, moduleId: 7 })
  })

  it('多模块:defaultModuleId 命中进默认;不命中弹 chooser', async () => {
    modulesMock.mockResolvedValue({ modules: [mod(1), mod(2)], defaultModuleId: 2 })
    let res = await useModule().enterInitial()
    expect(buildRoutesMock).toHaveBeenCalledWith(2)
    expect(res).toEqual({ chooser: false, moduleId: 2 })

    buildRoutesMock.mockClear()
    setActivePinia(createPinia()) // 新会话:currentModuleId 无残留
    modulesMock.mockResolvedValue({ modules: [mod(1), mod(2)], defaultModuleId: 99 }) // 99 不在 modules 里
    res = await useModule().enterInitial()
    expect(buildRoutesMock).not.toHaveBeenCalled()
    expect(res).toEqual({ chooser: true })
  })

  it('permissions() reject → permissionsLoaded=false 且不阻断;profile() reject → isSuperAdmin=false', async () => {
    modulesMock.mockResolvedValue({ modules: [], defaultModuleId: null })
    permissionsMock.mockRejectedValue(new Error('boom'))
    profileMock.mockRejectedValue(new Error('boom'))

    const auth = useAuthStore()
    const res = await useModule().enterInitial()

    expect(res).toEqual({ chooser: true }) // 未被两个 reject 阻断
    expect(auth.permissionsLoaded).toBe(false)
    expect(auth.permissionCodes).toEqual([])
    expect(auth.isSuperAdmin).toBe(false)
  })
})
