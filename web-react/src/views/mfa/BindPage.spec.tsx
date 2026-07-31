import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import '@/locales'

vi.mock('@/api', async (orig) => {
  const actual = await orig<typeof import('@/api')>()
  return {
    ...actual,
    mfaApi: {
      bindStart: vi.fn(),
      bindComplete: vi.fn(),
      recovery: vi.fn(),
      clear: vi.fn(),
    },
  }
})

import { mfaApi } from '@/api'
import BindPage from './BindPage'

const bindStartMock = vi.mocked(mfaApi.bindStart)
const bindCompleteMock = vi.mocked(mfaApi.bindComplete)

function mount(path = '/mfa/bind') {
  render(
    <AntdApp>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/mfa/bind" element={<BindPage />} />
        </Routes>
      </MemoryRouter>
    </AntdApp>,
  )
}

const fill = (label: string, value: string) =>
  fireEvent.change(screen.getByLabelText(label), { target: { value } })

/** antd two-char labels insert a space in the accessible name (`继 续`). */
const btn = (re: RegExp) => screen.getByRole('button', { name: re })

beforeEach(() => {
  vi.clearAllMocks()
  bindStartMock.mockResolvedValue({
    bindChallengeId: 'chal-1',
    otpauthUri: 'otpauth://totp/Tenon:ada?secret=ABCSEED',
    seed: 'ABCSEED',
  })
})

afterEach(() => {
  cleanup()
})

async function goToAuthenticatorStep() {
  fill('账号', 'ada')
  fill('当前密码', 'Secret1!')
  fireEvent.click(btn(/继\s*续/))
  await screen.findByText(/扫描下方二维码|Google Authenticator/)
  expect(bindStartMock).toHaveBeenCalledWith({
    account: 'ada',
    currentPassword: 'Secret1!',
  })
}

describe('MFA BindPage self-service + recovery-code integrity', () => {
  it('rejects an empty recoveryCodes response and never shows the recovery screen', async () => {
    bindCompleteMock.mockResolvedValue({ recoveryCodes: [] })
    mount()
    await goToAuthenticatorStep()

    fill('动态口令', '123456')
    fireEvent.click(btn(/验证并完成/))

    await waitFor(() => expect(bindCompleteMock).toHaveBeenCalledWith({
      bindChallengeId: 'chal-1',
      totpCode: '123456',
    }))
    await waitFor(() => {
      expect(document.body.textContent).toContain('设置未能返回恢复码，请重新开始设置。')
    })
    expect(screen.queryByText('保存恢复码')).toBeNull()
    expect(btn(/验证并完成/)).toBeTruthy()
  })

  it('rejects a missing recoveryCodes field the same way', async () => {
    bindCompleteMock.mockResolvedValue({})
    mount()
    await goToAuthenticatorStep()

    fill('动态口令', '654321')
    fireEvent.click(btn(/验证并完成/))

    await waitFor(() => {
      expect(document.body.textContent).toContain('设置未能返回恢复码，请重新开始设置。')
    })
    expect(screen.queryByText('保存恢复码')).toBeNull()
  })

  it('shows recovery codes when bindComplete returns a non-empty list', async () => {
    bindCompleteMock.mockResolvedValue({ recoveryCodes: ['code-one', 'code-two'] })
    mount()
    await goToAuthenticatorStep()

    fill('动态口令', '111111')
    fireEvent.click(btn(/验证并完成/))

    expect(await screen.findByText('保存恢复码')).toBeTruthy()
    expect(document.body.textContent).toContain('code-one')
    expect(document.body.textContent).toContain('code-two')
  })
})
