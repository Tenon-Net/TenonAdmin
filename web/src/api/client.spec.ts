import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useUserStore } from '@/stores/user'
import type { LoginOutput } from '@/types/api'

const transport = vi.fn<(request: Request) => Promise<Response>>()

let api: typeof import('./client')

function successfulRefresh(): LoginOutput {
  return {
    accessToken: 'new-access-token',
    expiresAt: '2099-01-01T00:00:00Z',
    refreshToken: '',
    refreshExpiresAt: '2099-01-02T00:00:00Z',
    userId: 1,
    account: 'admin',
    name: 'Admin',
    mustChangePassword: false,
    sessionMode: 'cookie',
    csrfRequired: true,
  }
}

beforeAll(async () => {
  vi.stubGlobal('fetch', (request: Request) => transport(request))
  api = await import('./client')
})

beforeEach(() => {
  setActivePinia(createPinia())
  transport.mockReset()
  document.cookie = 'tenon_csrf=; Max-Age=0; Path=/'
  document.cookie = 'tenon_csrf=%E0%A4%A; Path=/'
})

describe('CSRF Cookie', () => {
  it('写请求中间件遇到畸形百分号时仍发送原始 Cookie 值', async () => {
    transport.mockResolvedValueOnce(Response.json({ code: 0, data: true }))

    await api.client.POST('/api/v1/auth/logout')

    const request = transport.mock.calls[0]?.[0]
    expect(request?.headers.get('X-Tenon-CSRF')).toBe('%E0%A4%A')
  })

  it('Cookie 会话刷新遇到畸形百分号时仍完成请求并更新令牌', async () => {
    const user = useUserStore()
    user.cookieSession = true
    transport.mockResolvedValueOnce(Response.json({ code: 0, data: successfulRefresh() }))

    await expect(api.refreshOnce()).resolves.toBe(true)

    const request = transport.mock.calls[0]?.[0]
    expect(request?.url).toContain('/api/v1/auth/refresh')
    expect(request?.headers.get('X-Tenon-CSRF')).toBe('%E0%A4%A')
    expect(user.accessToken).toBe('new-access-token')
  })
})
