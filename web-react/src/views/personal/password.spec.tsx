import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { authApi, personalApi } from '@/api'
import { useUserStore } from '@/stores/user'

const { navigateMock } = vi.hoisted(() => ({ navigateMock: vi.fn() }))
vi.mock('react-router-dom', () => ({ useNavigate: () => navigateMock }))
// PasswordStrength 会拉 configApi.passwordPolicy;本测只验提交流,stub 掉。
vi.mock('@/components/PasswordStrength', () => ({ PasswordStrength: () => null }))

import PasswordPage from './password'

const mount = () => render(<AntdApp><PasswordPage /></AntdApp>)
const fill = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } })

beforeEach(() => {
  navigateMock.mockReset()
  vi.spyOn(personalApi, 'updatePassword').mockResolvedValue(true)
  vi.spyOn(authApi, 'logout').mockResolvedValue(true)
  useUserStore.setState({ accessToken: 't', refreshToken: 'r', userInfo: { userId: 1, account: 'a', name: 'n', mustChangePassword: false } })
})
afterEach(() => { cleanup(); vi.restoreAllMocks() })

describe('PasswordPage', () => {
  it('提交成功 → updatePassword + 登出 + 清会话 + 跳登录', async () => {
    mount()
    fill('原密码', 'oldpw')
    fill('新密码', 'NewPw123!')
    fill('确认新密码', 'NewPw123!')
    fireEvent.click(screen.getByRole('button', { name: /提\s*交/ }))
    await waitFor(() => expect(personalApi.updatePassword).toHaveBeenCalledWith({ oldPassword: 'oldpw', newPassword: 'NewPw123!' }))
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith('/login', { replace: true }))
    expect(useUserStore.getState().accessToken).toBe('') // 会话清空
  })

  it('两次不一致 → 校验拦截,不提交', async () => {
    mount()
    fill('原密码', 'oldpw')
    fill('新密码', 'NewPw123!')
    fill('确认新密码', 'different')
    fireEvent.click(screen.getByRole('button', { name: /提\s*交/ }))
    await screen.findByText('两次输入的新密码不一致')
    expect(personalApi.updatePassword).not.toHaveBeenCalled()
  })
})
