import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, type App } from 'vue'

const mocks = vi.hoisted(() => ({
  query: { error: '40015' } as Record<string, string>,
  replace: vi.fn(),
  user: { accessToken: '', refreshToken: '', cookieSession: false, setSession: vi.fn(), clear: vi.fn() },
  auth: { reset: vi.fn() },
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: mocks.query }),
  useRouter: () => ({ replace: mocks.replace }),
}))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('@/stores/user', () => ({ useUserStore: () => mocks.user }))
vi.mock('@/stores/auth', () => ({ useAuthStore: () => mocks.auth }))
vi.mock('@/router', () => ({ resetRouter: vi.fn() }))
vi.mock('@/api', () => ({ externalAuthApi: { exchange: vi.fn() }, ApiError: class extends Error {} }))
vi.mock('@/utils/error', () => ({ translateError: (error: unknown) => String(error) }))
vi.mock('@/components/AppIcon.vue', () => ({ default: { render: () => null } }))

import CallbackPage from './callback.vue'

let app: App<Element> | undefined

beforeEach(() => {
  vi.useFakeTimers()
  vi.clearAllMocks()
  mocks.query = { error: '40015' }
})

afterEach(() => {
  app?.unmount()
  app = undefined
  vi.useRealTimers()
})

describe('OAuth callback', () => {
  it('卸载后清理失败跳转定时器', async () => {
    const host = document.createElement('div')
    app = createApp(CallbackPage)
    app.mount(host)
    await Promise.resolve()

    app.unmount()
    app = undefined
    await vi.advanceTimersByTimeAsync(3000)

    expect(mocks.replace).not.toHaveBeenCalled()
  })
})
