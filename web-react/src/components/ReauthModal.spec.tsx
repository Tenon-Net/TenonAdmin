import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { App as AntdApp } from 'antd'
import '@/locales'
import { authApi } from '@/api'
import { requestReauth } from '@/api/reauthGate'
import { ReauthModal } from './ReauthModal'

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('ReauthModal', () => {
  it('提交中禁用取消,请求完成后才结算', async () => {
    let release!: () => void
    vi.spyOn(authApi, 'reauth').mockImplementation(() => new Promise<void>((resolve) => { release = resolve }))
    render(<AntdApp><ReauthModal /></AntdApp>)

    const result = requestReauth()
    fireEvent.change(await screen.findByPlaceholderText('6 位动态口令'), { target: { value: '123456' } })
    fireEvent.click(screen.getByRole('button', { name: '验证并继续' }))
    const cancel = screen.getByRole('button', { name: /取\s*消/ }) as HTMLButtonElement
    await waitFor(() => expect(cancel.disabled).toBe(true))
    fireEvent.click(cancel)
    let settled = false
    void result.then(() => { settled = true })
    await Promise.resolve()
    expect(settled).toBe(false)

    release()
    await expect(result).resolves.toBe(true)
  })
})
