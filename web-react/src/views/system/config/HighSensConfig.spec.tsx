import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App as AntdApp } from 'antd'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import '@/locales'

const list = vi.hoisted(() => vi.fn())
const remove = vi.hoisted(() => vi.fn())

vi.mock('@/api', () => ({
  highSensApi: {
    list,
    add: vi.fn(),
    remove,
  },
}))

import HighSensConfig from './HighSensConfig'

const unsafeId = '9007199254740993'

beforeEach(() => {
  list.mockResolvedValue({
    defaults: [],
    customs: [{ id: unsafeId, permissionCode: 'POST:/api/v1/example', remark: null }],
  })
  remove.mockResolvedValue(undefined)
})

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
})

describe('HighSensConfig large ids', () => {
  it('删除时原样回传后端给出的十进制字符串 Id', async () => {
    render(<AntdApp><HighSensConfig /></AntdApp>)

    await screen.findByText('POST:/api/v1/example')
    fireEvent.click(screen.getByRole('button', { name: '删除' }))

    await waitFor(() => expect(remove).toHaveBeenCalledWith(unsafeId))
  })
})
