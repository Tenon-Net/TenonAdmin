import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App as AntdApp } from 'antd'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import '@/locales'
import type { WfInstanceListItem } from '@/types/workflow'

const navigate = vi.hoisted(() => vi.fn())
const get = vi.hoisted(() => vi.fn())
const forceTransfer = vi.hoisted(() => vi.fn())
const terminate = vi.hoisted(() => vi.fn())
const skip = vi.hoisted(() => vi.fn())
const fixture = vi.hoisted(() => ({
  instanceId: '9007199254740993' as string | number,
  userId: '9007199254740999' as string | number,
}))

vi.mock('react-router-dom', () => ({ useNavigate: () => navigate }))
vi.mock('@/stores/auth', () => ({ useHasPerm: () => () => true }))
vi.mock('@/api/workflow', () => ({
  wfInstanceApi: { monitor: vi.fn(), get, forceTransfer, terminate, skip },
}))
vi.mock('@/components/DataTable', () => ({
  DataTable: ({
    columns,
    onRowClick,
  }: {
    columns: Array<{ key?: string; render?: (dom: ReactNode, row: WfInstanceListItem) => ReactNode }>
    onRowClick?: (row: WfInstanceListItem) => void
  }) => {
    const row = { id: fixture.instanceId, status: 1, definitionName: '请假' } as WfInstanceListItem
    const op = columns.find((column) => column.key === 'op')
    return (
      <div>
        <button type="button" onClick={() => onRowClick?.(row)}>row-click</button>
        {op?.render?.('', row)}
      </div>
    )
  },
}))
vi.mock('@/components/UserSelect', () => ({
  UserSelect: ({ onChange, placeholder }: { onChange?: (value: string | number) => void; placeholder?: string }) => (
    <button type="button" aria-label={placeholder} onClick={() => onChange?.(fixture.userId)}>pick-user</button>
  ),
}))

import MonitorPage from './index'

const snowflake = '9007199254740993'
const targetUser = '9007199254740999'

beforeEach(() => {
  vi.clearAllMocks()
  fixture.instanceId = snowflake
  fixture.userId = targetUser
  get.mockResolvedValue({ currentTasks: [{ nodeId: 'approval', nodeName: '审批' }] })
  forceTransfer.mockResolvedValue({})
  terminate.mockResolvedValue({})
  skip.mockResolvedValue({})
})

afterEach(cleanup)

function renderPage() {
  return render(<AntdApp><MonitorPage /></AntdApp>)
}

async function fillReason(text: string) {
  const input = document.querySelector('textarea')
  if (!input) throw new Error('missing reason')
  fireEvent.change(input, { target: { value: text } })
}

describe('workflow monitor large ids', () => {
  it('keeps the instance id in the detail url', () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: '查看' }))
    expect(navigate).toHaveBeenCalledWith(`/workflow/instance/${snowflake}/detail`)
    fireEvent.click(screen.getByRole('button', { name: 'row-click' }))
    expect(navigate).toHaveBeenLastCalledWith(`/workflow/instance/${snowflake}/detail`)
  })

  it('sends the original instance id and target user id on force transfer', async () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: '强制转办' }))
    await waitFor(() => expect(get).toHaveBeenCalledWith(snowflake))
    fireEvent.click(screen.getByRole('button', { name: '转给' }))
    await fillReason('转给财务')
    fireEvent.click(screen.getByRole('button', { name: '确 定' }))
    await waitFor(() => expect(forceTransfer).toHaveBeenCalledWith({
      instanceId: snowflake,
      toUserId: targetUser,
      reason: '转给财务',
      nodeId: 'approval',
    }))
  })

  it('sends the original instance id on terminate', async () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: '终止' }))
    await fillReason('结束')
    fireEvent.click(screen.getByRole('button', { name: '确 定' }))
    await waitFor(() => expect(terminate).toHaveBeenCalledWith({ instanceId: snowflake, reason: '结束' }))
    expect(get).not.toHaveBeenCalled()
  })

  it('sends the original instance id on skip', async () => {
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: '跳过节点' }))
    await waitFor(() => expect(get).toHaveBeenCalledWith(snowflake))
    await waitFor(() => expect(screen.getByRole('button', { name: '确 定' })).toBeTruthy())
    await fillReason('跳过')
    fireEvent.click(screen.getByRole('button', { name: '确 定' }))
    await waitFor(() => expect(skip).toHaveBeenCalledWith({
      instanceId: snowflake,
      reason: '跳过',
      nodeId: 'approval',
    }))
  })

  it('keeps safe integer ids as numbers', async () => {
    fixture.instanceId = 42
    fixture.userId = 7
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: '强制转办' }))
    await waitFor(() => expect(get).toHaveBeenCalledWith(42))
    fireEvent.click(screen.getByRole('button', { name: '转给' }))
    await fillReason('转办')
    fireEvent.click(screen.getByRole('button', { name: '确 定' }))
    await waitFor(() => expect(forceTransfer).toHaveBeenCalledWith({
      instanceId: 42,
      toUserId: 7,
      reason: '转办',
      nodeId: 'approval',
    }))
  })

  it('does not submit an unsafe number id', async () => {
    fixture.instanceId = Number.MAX_SAFE_INTEGER + 2
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: '终止' }))
    await fillReason('不能交')
    fireEvent.click(screen.getByRole('button', { name: '确 定' }))
    await waitFor(() => expect(document.body.textContent).toContain('编号已丢失精度，未提交'))
    expect(terminate).not.toHaveBeenCalled()
  })
})
