import { describe, it, expect, beforeEach } from 'vitest'
import type { LoginOutput } from '@/types/api'
import { useUserStore, isLoggedIn } from './user'

const LOGIN: LoginOutput = {
  accessToken: 'at',
  expiresAt: '2030-01-01T00:00:00Z',
  refreshToken: 'rt',
  refreshExpiresAt: '2030-01-02T00:00:00Z',
  userId: 1,
  account: 'admin',
  name: '超管',
  mustChangePassword: false,
}

beforeEach(() => {
  useUserStore.getState().clear()
  localStorage.clear()
})

describe('useUserStore', () => {
  it('setSession 落地令牌对与用户信息;isLoggedIn 随之为真', () => {
    expect(isLoggedIn(useUserStore.getState())).toBe(false)
    useUserStore.getState().setSession(LOGIN)
    const s = useUserStore.getState()
    expect(s.accessToken).toBe('at')
    expect(s.refreshToken).toBe('rt')
    expect(s.userInfo).toEqual({ userId: 1, account: 'admin', name: '超管', mustChangePassword: false })
    expect(isLoggedIn(s)).toBe(true)
  })

  it('后端漏给 mustChangePassword 时归一为 false,而不是 undefined', () => {
    // 守卫写的是 `if (mustChangePassword)`,undefined 也能过 —— 但它会被原样持久化,
    // 之后任何 `=== false` 的判断都会静默走错分支。归一在入口做一次。
    const { mustChangePassword: _omit, ...partial } = LOGIN
    useUserStore.getState().setSession(partial as LoginOutput)
    expect(useUserStore.getState().userInfo?.mustChangePassword).toBe(false)
  })

  it('clear 清空会话', () => {
    useUserStore.getState().setSession(LOGIN)
    useUserStore.getState().clear()
    const s = useUserStore.getState()
    expect(s.accessToken).toBe('')
    expect(s.refreshToken).toBe('')
    expect(s.userInfo).toBeNull()
    expect(isLoggedIn(s)).toBe(false)
  })

  it('持久化白名单:三项全落盘,action 不落盘', () => {
    useUserStore.getState().setSession(LOGIN)
    const saved = JSON.parse(localStorage.getItem('user')!).state
    expect(Object.keys(saved).sort()).toEqual(['accessToken', 'refreshToken', 'userInfo'])
  })
})
