import { describe, it, expect, beforeEach } from 'vitest'
import { createApp } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import { createPersistedState } from 'pinia-plugin-persistedstate'
import { isCookieSession, useUserStore } from './user'
import type { LoginOutput } from '@/types/api'

type PersistedUserStore = ReturnType<typeof useUserStore> & { $persist: () => void }

function sample(partial: Partial<LoginOutput> = {}): LoginOutput {
  return {
    accessToken: 'at',
    expiresAt: '2099-01-01T00:00:00Z',
    refreshToken: 'rt',
    refreshExpiresAt: '2099-01-02T00:00:00Z',
    userId: 1,
    account: 'admin',
    name: 'Admin',
    mustChangePassword: false,
    ...partial,
  }
}

// happy-dom 在 Node 22+ 下 window.localStorage 可能为 undefined,与 app.spec 同款内存 Storage 桩。
function makeMemoryStorage(): Storage {
  const data = new Map<string, string>()
  return {
    getItem: (k: string) => data.get(k) ?? null,
    setItem: (k: string, v: string) => void data.set(k, v),
    removeItem: (k: string) => void data.delete(k),
    clear: () => data.clear(),
    key: (i: number) => Array.from(data.keys())[i] ?? null,
    get length() {
      return data.size
    },
  } as Storage
}

function mountUserStore(storage: Storage): PersistedUserStore {
  const pinia = createPinia()
  // 显式注入 storage:默认 window.localStorage 在本测试环境不可靠
  pinia.use(createPersistedState({ storage }))
  createApp({}).use(pinia)
  setActivePinia(pinia)
  return useUserStore() as PersistedUserStore
}

beforeEach(() => {
  setActivePinia(createPinia())
})

describe('isCookieSession', () => {
  it('sessionMode=cookie / csrfRequired=true → Level3', () => {
    expect(isCookieSession(sample({ sessionMode: 'cookie', refreshToken: '' }))).toBe(true)
    expect(isCookieSession(sample({ csrfRequired: true, refreshToken: '' }))).toBe(true)
  })

  it('sessionMode=body / 显式 false → 非 Level3', () => {
    expect(isCookieSession(sample({ sessionMode: 'body' }))).toBe(false)
    expect(isCookieSession(sample({ csrfRequired: false }))).toBe(false)
  })

  it('无声明时用空 refresh + 有 access 兜底', () => {
    expect(isCookieSession(sample({ refreshToken: '', accessToken: 'at' }))).toBe(true)
    expect(isCookieSession(sample({ refreshToken: 'rt', accessToken: 'at' }))).toBe(false)
  })
})

describe('useUserStore Level3 persist', () => {
  it('body 模式仍持久化 access/refresh', () => {
    const storage = makeMemoryStorage()
    const user = mountUserStore(storage)
    user.setSession(sample({ sessionMode: 'body', csrfRequired: false }))
    user.$persist()
    expect(user.cookieSession).toBe(false)
    expect(user.accessToken).toBe('at')
    expect(user.refreshToken).toBe('rt')

    const raw = storage.getItem('user')
    expect(raw).toBeTruthy()
    const parsed = JSON.parse(raw!) as { accessToken: string; refreshToken: string; cookieSession: boolean }
    expect(parsed.accessToken).toBe('at')
    expect(parsed.refreshToken).toBe('rt')
    expect(parsed.cookieSession).toBe(false)
  })

  it('cookie 模式不把令牌写入 localStorage', () => {
    const storage = makeMemoryStorage()
    const user = mountUserStore(storage)
    user.setSession(sample({ sessionMode: 'cookie', csrfRequired: true, refreshToken: '' }))
    user.$persist()
    expect(user.cookieSession).toBe(true)
    expect(user.accessToken).toBe('at')
    expect(user.refreshToken).toBe('')

    const raw = storage.getItem('user')
    expect(raw).toBeTruthy()
    const parsed = JSON.parse(raw!) as {
      accessToken: string
      refreshToken: string
      cookieSession: boolean
      userInfo: unknown
    }
    expect(parsed.cookieSession).toBe(true)
    expect(parsed.accessToken).toBe('')
    expect(parsed.refreshToken).toBe('')
    expect(parsed.userInfo).toBeNull()
  })

  it('clear 清掉 cookieSession 与令牌', () => {
    const storage = makeMemoryStorage()
    const user = mountUserStore(storage)
    user.setSession(sample({ sessionMode: 'cookie', refreshToken: '' }))
    user.clear()
    expect(user.accessToken).toBe('')
    expect(user.refreshToken).toBe('')
    expect(user.cookieSession).toBe(false)
    expect(user.userInfo).toBeNull()
  })
})
