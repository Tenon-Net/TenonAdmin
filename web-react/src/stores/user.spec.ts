import { describe, it, expect, beforeEach } from 'vitest'
import type { LoginOutput } from '@/types/api'
import { useUserStore, isLoggedIn, isCookieSession } from './user'

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

const LOGIN_COOKIE: LoginOutput = {
  ...LOGIN,
  refreshToken: '',
  sessionMode: 'cookie',
  csrfRequired: true,
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
    expect(s.sessionMode).toBe('body')
    expect(s.csrfRequired).toBe(false)
    expect(s.userInfo).toEqual({ userId: 1, account: 'admin', name: '超管', mustChangePassword: false })
    expect(isLoggedIn(s)).toBe(true)
    expect(isCookieSession(s)).toBe(false)
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
    expect(s.sessionMode).toBeNull()
    expect(s.csrfRequired).toBe(false)
    expect(isLoggedIn(s)).toBe(false)
  })

  it('非 Level3 持久化白名单:令牌 + 用户信息 + 模式落盘,action 不落盘', () => {
    useUserStore.getState().setSession(LOGIN)
    const saved = JSON.parse(localStorage.getItem('user')!).state
    expect(saved.accessToken).toBe('at')
    expect(saved.refreshToken).toBe('rt')
    expect(saved.userInfo).toEqual({ userId: 1, account: 'admin', name: '超管', mustChangePassword: false })
    expect(saved.sessionMode).toBe('body')
    expect(saved.csrfRequired).toBe(false)
  })

  it('Level3 cookie 会话:access 仅内存,refresh 不落盘;localStorage 无令牌', () => {
    useUserStore.getState().setSession(LOGIN_COOKIE)
    const s = useUserStore.getState()
    expect(s.accessToken).toBe('at')
    expect(s.refreshToken).toBe('')
    expect(s.sessionMode).toBe('cookie')
    expect(s.csrfRequired).toBe(true)
    expect(isCookieSession(s)).toBe(true)
    expect(isLoggedIn(s)).toBe(true)

    const saved = JSON.parse(localStorage.getItem('user')!).state
    expect(saved.accessToken).toBe('')
    expect(saved.refreshToken).toBe('')
    expect(saved.sessionMode).toBe('cookie')
    expect(saved.csrfRequired).toBe(true)
    expect(saved.userInfo?.account).toBe('admin')
    // 整段 JSON 不得含 access/refresh 明文令牌值
    const raw = localStorage.getItem('user')!
    expect(raw).not.toContain('"at"')
    expect(raw).not.toContain('refreshToken":"rt')
  })

  it('Level3:从 body 切到 cookie 后,旧 localStorage 令牌被清空', () => {
    useUserStore.getState().setSession(LOGIN)
    expect(JSON.parse(localStorage.getItem('user')!).state.accessToken).toBe('at')
    useUserStore.getState().setSession(LOGIN_COOKIE)
    const saved = JSON.parse(localStorage.getItem('user')!).state
    expect(saved.accessToken).toBe('')
    expect(saved.refreshToken).toBe('')
    expect(saved.sessionMode).toBe('cookie')
  })

  it('sessionMode=cookie 且 body 无 refresh 时判定为 cookie 会话', () => {
    useUserStore.getState().setSession({
      ...LOGIN,
      refreshToken: '',
      sessionMode: 'cookie',
    })
    expect(isCookieSession(useUserStore.getState())).toBe(true)
    expect(useUserStore.getState().refreshToken).toBe('')
  })
})
