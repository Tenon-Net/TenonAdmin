import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App as AntdApp } from 'antd'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import '@/locales'

vi.mock('@/components/DataTable', () => ({
  DataTable: ({ toolbar }: { toolbar?: ReactNode }) => <div>{toolbar}</div>,
}))
vi.mock('@/components/Can', () => ({ Can: ({ children }: { children: ReactNode }) => children }))
vi.mock('@/components/FormContainer', () => ({
  FormContainer: ({ open, children, onConfirm }: {
    open: boolean
    children: ReactNode
    onConfirm?: () => void
  }) => open ? <div>{children}<button type="button" onClick={() => onConfirm?.()}>save-rule</button></div> : null,
}))
vi.mock('@/components/UserSelect', () => ({
  UserSelect: ({ value, onChange, placeholder }: {
    value?: number | string
    onChange?: (value: string) => void
    placeholder?: string
  }) => (
    <input
      aria-label={placeholder}
      value={value ?? ''}
      onChange={(event) => onChange?.(event.target.value)}
    />
  ),
}))
vi.mock('@/stores/auth', () => ({ useHasPerm: () => () => true }))
vi.mock('@/hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: vi.fn() }) }))
vi.mock('@/api/workflow', () => ({
  wfDelegationApi: {
    page: vi.fn().mockResolvedValue({ items: [], total: 0 }),
    add: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
  },
}))

import { wfDelegationApi } from '@/api/workflow'
import WfDelegationPage from './index'

beforeEach(() => vi.clearAllMocks())
afterEach(cleanup)

describe('WfDelegationPage 请求键', () => {
  it('网络失败原载荷复用请求键，编辑载荷后换新键', async () => {
    vi.mocked(wfDelegationApi.add)
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValue({} as never)
    render(<AntdApp><WfDelegationPage /></AntdApp>)

    fireEvent.click(screen.getByRole('button', { name: /新\s*增/ }))
    fireEvent.change(screen.getByLabelText('原责任人'), { target: { value: '9000000000000000001' } })
    fireEvent.change(screen.getByLabelText('委托给'), { target: { value: '9000000000000000002' } })
    fireEvent.click(screen.getByRole('button', { name: 'save-rule' }))
    await waitFor(() => expect(wfDelegationApi.add).toHaveBeenCalledTimes(1))
    const first = vi.mocked(wfDelegationApi.add).mock.calls[0]![0].requestId

    fireEvent.click(screen.getByRole('button', { name: 'save-rule' }))
    await waitFor(() => expect(wfDelegationApi.add).toHaveBeenCalledTimes(2))
    expect(vi.mocked(wfDelegationApi.add).mock.calls[1]![0].requestId).toBe(first)

    fireEvent.change(screen.getByLabelText('委托给'), { target: { value: '9000000000000000003' } })
    fireEvent.click(screen.getByRole('button', { name: 'save-rule' }))
    await waitFor(() => expect(wfDelegationApi.add).toHaveBeenCalledTimes(3))
    expect(vi.mocked(wfDelegationApi.add).mock.calls[2]![0]).toMatchObject({
      originalUserId: '9000000000000000001',
      delegateUserId: '9000000000000000003',
    })
    expect(vi.mocked(wfDelegationApi.add).mock.calls[2]![0].requestId).not.toBe(first)
  })
})
