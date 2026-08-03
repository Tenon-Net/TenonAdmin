import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import { StrictMode } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'

vi.mock('@/api', () => ({ externalAuthApi: { exchange: vi.fn() } }))

import { externalAuthApi } from '@/api'
import CallbackPage from './CallbackPage'
import { useUserStore } from '@/stores/user'

const exchangeMock = vi.mocked(externalAuthApi.exchange)

function mountAt(path: string) {
  render(
    <StrictMode>
      <AntdApp>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/oauth/callback" element={<CallbackPage />} />
            <Route path="/" element={<div>HOME</div>} />
            <Route path="/login" element={<div>LOGIN</div>} />
            <Route path="/personal/bindings" element={<div>BINDINGS</div>} />
          </Routes>
        </MemoryRouter>
      </AntdApp>
    </StrictMode>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  useUserStore.setState({ accessToken: '', refreshToken: '', userInfo: null })
})

afterEach(() => {
  cleanup()
  vi.useRealTimers()
})

describe('OAuth callback', () => {
  it('exchanges a ticket, persists the session, then enters the app', async () => {
    exchangeMock.mockResolvedValue({
      accessToken: 'access', refreshToken: 'refresh', userId: 1, account: 'ada', name: 'Ada',
      expiresAt: '2030-01-01T00:00:00Z', refreshExpiresAt: '2030-02-01T00:00:00Z', mustChangePassword: false,
    })
    mountAt('/oauth/callback?ticket=one-time-ticket')

    await waitFor(() => expect(screen.getByText('HOME')).toBeTruthy())
    expect(exchangeMock).toHaveBeenCalledWith('one-time-ticket')
    expect(useUserStore.getState().accessToken).toBe('access')
  })

  it('returns a completed binding to the bindings page without exchanging a ticket', async () => {
    mountAt('/oauth/callback?bind=github')

    expect(await screen.findByText('BINDINGS')).toBeTruthy()
    expect(exchangeMock).not.toHaveBeenCalled()
  })

  it('maps an OAuth error code and returns to sign-in after a short delay', async () => {
    vi.useFakeTimers()
    mountAt('/oauth/callback?error=40016')

    await act(async () => { await Promise.resolve() })
    expect(screen.getByText('该第三方账号尚未绑定系统账号，请先登录本系统账号以完成绑定')).toBeTruthy()
    await act(async () => { await vi.advanceTimersByTimeAsync(2600) })
    expect(screen.getByText('LOGIN')).toBeTruthy()
  })

  it('forwards pending-link to the login page for on-the-spot binding', async () => {
    mountAt('/oauth/callback?pendingLink=tok123&provider=github')

    expect(await screen.findByText('LOGIN')).toBeTruthy()
    expect(exchangeMock).not.toHaveBeenCalled()
  })

  it('drops residual SPA session before forwarding pending-link (unbind then SSO must re-auth)', async () => {
    useUserStore.setState({
      accessToken: 'stale-access',
      refreshToken: 'stale-refresh',
      userInfo: { userId: 1, account: 'superAdmin', name: '超管', mustChangePassword: false },
      sessionMode: 'body',
      csrfRequired: false,
    })
    mountAt('/oauth/callback?pendingLink=tok456&provider=github')

    expect(await screen.findByText('LOGIN')).toBeTruthy()
    expect(useUserStore.getState().accessToken).toBe('')
    expect(useUserStore.getState().refreshToken).toBe('')
    expect(exchangeMock).not.toHaveBeenCalled()
  })
})
