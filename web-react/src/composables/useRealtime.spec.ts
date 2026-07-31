import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, cleanup } from '@testing-library/react'
import { createElement } from 'react'
import { App as AntdApp } from 'antd'
import { MemoryRouter } from 'react-router-dom'
import '@/locales' // 副作用:初始化 i18n,否则 t('realtime.forcedLogout') 返回 key

const navigate = vi.fn()
vi.mock('react-router-dom', async (orig) => ({
  ...(await orig<typeof import('react-router-dom')>()),
  useNavigate: () => navigate,
}))

// 气隙(vite.config test 硬约束:任何测试都不得触网):mock 掉 SignalR,既不真发协商,
// 又捕获注册进 conn.on 的事件回调,好直接测 force-logout 收尾(不依赖真 Hub)。
const { conn, handlers } = vi.hoisted(() => {
  const handlers: Record<string, () => void> = {}
  const conn = {
    on: (event: string, cb: () => void) => {
      handlers[event] = cb
    },
    start: vi.fn(() => Promise.resolve()),
    stop: vi.fn(() => Promise.resolve()),
  }
  return { conn, handlers }
})
vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl() {
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      return conn
    }
  },
  LogLevel: { Warning: 3 },
}))

import { beginVoluntaryLogout, noticeBus, useRealtime } from './useRealtime'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'
import { useTabsStore } from '@/stores/tabs'

// 探针:仅调用 useRealtime()(挂载即 start,注册 force-logout/notice-changed 回调)。
function Probe() {
  useRealtime()
  return null
}

beforeEach(() => {
  vi.clearAllMocks()
  localStorage.clear()
  useAuthStore.getState().reset()
  useUserStore.getState().clear()
  Object.keys(handlers).forEach((k) => delete handlers[k])
})
afterEach(cleanup)

// SignalR 连接的传输层依赖真 Hub、happy-dom 无从测;但注册进 conn.on 的回调可测(见下)。
describe('noticeBus', () => {
  it('on → emit 调用监听器;退订后不再调用', () => {
    const fn = vi.fn()
    const off = noticeBus.on(fn)
    noticeBus.emit()
    expect(fn).toHaveBeenCalledTimes(1)
    off()
    noticeBus.emit()
    expect(fn).toHaveBeenCalledTimes(1) // 退订后 emit 不再增
  })

  it('多订阅者各自收到', () => {
    const a = vi.fn()
    const b = vi.fn()
    const offA = noticeBus.on(a)
    const offB = noticeBus.on(b)
    noticeBus.emit()
    expect(a).toHaveBeenCalledTimes(1)
    expect(b).toHaveBeenCalledTimes(1)
    offA()
    offB()
  })
})

describe('useRealtime force-logout', () => {
  it('force-logout 回调:彻底清会话(userStore + authStore/非固定标签)+ 跳登录', () => {
    // 已登录 + 有菜单树 + 一条非固定标签(clearTabs 保留 affix/pinned,故用非固定的才验得到清空)
    useUserStore.setState({ accessToken: 't', refreshToken: 'r', userInfo: { userId: 1, account: 'a', name: 'n', mustChangePassword: false } })
    useAuthStore.setState({ menuTree: [{ id: 1, parentId: 0, type: 2, title: 't', path: '/x', sort: 0, visible: true, children: [] }], routesReady: true } as never)
    useTabsStore.setState({ tabs: [{ path: '/x', fullPath: '/x', title: 'X', affix: false }] } as never)

    render(createElement(AntdApp, null, createElement(MemoryRouter, null, createElement(Probe))))

    // start() 应已把 force-logout 回调注册进 conn.on(mock 捕获)
    expect(handlers['force-logout']).toBeTruthy()
    handlers['force-logout']() // 服务端强制下线

    expect(useUserStore.getState().accessToken).toBe('') // userStore.clear
    expect(useTabsStore.getState().tabs).toHaveLength(0) // authStore.reset → clearTabs
    expect(useAuthStore.getState().menuTree).toHaveLength(0) // authStore.reset
    expect(navigate).toHaveBeenCalledWith('/login', { replace: true })
  })

  it('beginVoluntaryLogout 先断连;迟到的 force-logout 仍清会话(自愿标记静默,不谎报被踢)', async () => {
    const warning = vi.fn()
    vi.spyOn(AntdApp, 'useApp').mockReturnValue({
      message: { warning, success: vi.fn(), error: vi.fn(), info: vi.fn(), loading: vi.fn(), open: vi.fn() },
      modal: {} as never,
      notification: {} as never,
    } as never)

    useUserStore.setState({ accessToken: 't', refreshToken: 'r', userInfo: { userId: 1, account: 'a', name: 'n', mustChangePassword: false } })
    useAuthStore.setState({ routesReady: true } as never)

    render(createElement(AntdApp, null, createElement(MemoryRouter, null, createElement(Probe))))
    expect(handlers['force-logout']).toBeTruthy()

    await beginVoluntaryLogout()
    expect(conn.stop).toHaveBeenCalled()

    handlers['force-logout']()
    expect(useUserStore.getState().accessToken).toBe('')
    expect(navigate).toHaveBeenCalledWith('/login', { replace: true })
    expect(warning).not.toHaveBeenCalled()
  })

  it('非自愿 force-logout 仍提示强制下线', () => {
    const warning = vi.fn()
    vi.spyOn(AntdApp, 'useApp').mockReturnValue({
      message: { warning, success: vi.fn(), error: vi.fn(), info: vi.fn(), loading: vi.fn(), open: vi.fn() },
      modal: {} as never,
      notification: {} as never,
    } as never)

    useUserStore.setState({ accessToken: 't', refreshToken: 'r', userInfo: { userId: 1, account: 'a', name: 'n', mustChangePassword: false } })
    render(createElement(AntdApp, null, createElement(MemoryRouter, null, createElement(Probe))))
    handlers['force-logout']()
    expect(warning).toHaveBeenCalled()
  })
})
