import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { createApp, defineComponent, h, nextTick } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { createI18n } from 'vue-i18n'

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

const warning = vi.fn()
vi.mock('naive-ui', async (orig) => {
  const actual = await orig<typeof import('naive-ui')>()
  return {
    ...actual,
    useMessage: () => ({ warning, success: vi.fn(), error: vi.fn(), info: vi.fn(), loading: vi.fn() }),
  }
})

import { beginVoluntaryLogout, useRealtime } from './useRealtime'
import { useUserStore } from '@/stores/user'

function mountRealtime() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/login', component: { template: '<div />' } },
    ],
  })
  const i18n = createI18n({
    legacy: false,
    locale: 'zh-CN',
    messages: { 'zh-CN': { realtime: { forcedLogout: '您已被强制下线' } } },
  })
  const pinia = createPinia()
  setActivePinia(pinia)
  useUserStore().$patch({
    accessToken: 't',
    refreshToken: 'r',
    userInfo: { userId: 1, account: 'a', name: 'n', mustChangePassword: false },
  } as never)

  const Host = defineComponent({
    setup() {
      const { start } = useRealtime()
      start()
      return () => h('div')
    },
  })
  const app = createApp(Host)
  app.use(pinia)
  app.use(router)
  app.use(i18n)
  const el = document.createElement('div')
  app.mount(el)
  return { app, router }
}

beforeEach(() => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
  Object.keys(handlers).forEach((k) => delete handlers[k])
})

afterEach(() => {
  document.body.innerHTML = ''
})

describe('useRealtime force-logout', () => {
  it('非自愿 force-logout:清会话 + 弹强制下线', async () => {
    const { app } = mountRealtime()
    await nextTick()
    expect(handlers['force-logout']).toBeTruthy()

    handlers['force-logout']()
    await nextTick()
    // force-logout 里动态 import auth/router 是微任务,再等一拍
    await Promise.resolve()
    await nextTick()

    expect(useUserStore().accessToken).toBe('')
    expect(warning).toHaveBeenCalled()
    app.unmount()
  })

  it('beginVoluntaryLogout 后 force-logout 静默:清会话但不弹强制下线', async () => {
    const { app } = mountRealtime()
    await nextTick()

    await beginVoluntaryLogout()
    expect(conn.stop).toHaveBeenCalled()

    handlers['force-logout']()
    await nextTick()
    await Promise.resolve()
    await nextTick()

    expect(useUserStore().accessToken).toBe('')
    expect(warning).not.toHaveBeenCalled()
    app.unmount()
  })
})
