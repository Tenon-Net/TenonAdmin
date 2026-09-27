import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App as AntdApp } from 'antd'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import '@/locales'
import { createApprovalNode, createDefaultModel } from '@/workflow/model'
import type { WfStartableDefinitionDetail } from '@/types/workflow'

vi.mock('@/components/AppIcon', () => ({ AppIcon: () => null }))
vi.mock('@/components/UserSelect', () => ({ UserSelect: () => <div>self-select-control</div> }))
vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
  useSearchParams: () => [new URLSearchParams()],
}))
vi.mock('@/api/workflow', () => ({
  wfInstanceApi: {
    startable: vi.fn(),
    startableDetail: vi.fn(),
    start: vi.fn(),
  },
}))

import { wfInstanceApi } from '@/api/workflow'
import WfStartPage from './index'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

function definition(id: number, nodeName: string): WfStartableDefinitionDetail {
  const model = createDefaultModel()
  model.root.next = createApprovalNode({
    id: `ap-${id}`,
    name: nodeName,
    props: { assignee: { provider: 'selfSelect' } },
  })
  return { id, name: nodeName, model } as WfStartableDefinitionDetail
}

async function chooseDefinition(label: string) {
  fireEvent.mouseDown(document.querySelector('.ant-select-content')!)
  const option = await screen.findByTitle(label)
  fireEvent.click(option)
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(wfInstanceApi.startable).mockResolvedValue([
    { id: 1, name: '定义 A' },
    { id: 2, name: '定义 B' },
  ] as never)
})
afterEach(cleanup)

describe('WfStartPage', () => {
  it('较晚返回的旧定义详情不能覆盖当前选择', async () => {
    const a = deferred<WfStartableDefinitionDetail>()
    const b = deferred<WfStartableDefinitionDetail>()
    vi.mocked(wfInstanceApi.startableDetail)
      .mockReturnValueOnce(a.promise)
      .mockReturnValueOnce(b.promise)
    render(<AntdApp><WfStartPage /></AntdApp>)

    await waitFor(() => expect(wfInstanceApi.startable).toHaveBeenCalled())
    await chooseDefinition('定义 A')
    await chooseDefinition('定义 B')
    b.resolve(definition(2, 'B 审批'))
    expect(await screen.findByText('B 审批')).toBeTruthy()

    a.resolve(definition(1, 'A 审批'))
    await Promise.resolve()
    expect(screen.queryByText('A 审批')).toBeNull()
    expect(screen.getByRole('button', { name: /发\s*起/ })).toHaveProperty('disabled', false)
  })

  it('不确定请求重试复用请求键,载荷变化后才换键', async () => {
    vi.mocked(wfInstanceApi.startableDetail).mockResolvedValue(definition(1, 'A 审批'))
    vi.mocked(wfInstanceApi.start)
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValue({ instanceId: 5, instanceStatus: 1 })
    render(<AntdApp><WfStartPage /></AntdApp>)

    await waitFor(() => expect(wfInstanceApi.startable).toHaveBeenCalled())
    await chooseDefinition('定义 A')
    const submit = await screen.findByRole('button', { name: /发\s*起/ })
    await waitFor(() => expect(submit).toHaveProperty('disabled', false))
    fireEvent.click(submit)
    await waitFor(() => expect(wfInstanceApi.start).toHaveBeenCalledTimes(1))
    const first = vi.mocked(wfInstanceApi.start).mock.calls[0]![0].requestId

    await waitFor(() => expect(submit.className).not.toContain('ant-btn-loading'))
    fireEvent.click(submit)
    await waitFor(() => expect(wfInstanceApi.start).toHaveBeenCalledTimes(2))
    expect(vi.mocked(wfInstanceApi.start).mock.calls[1]![0].requestId).toBe(first)

    await waitFor(() => expect(submit.className).not.toContain('ant-btn-loading'))
    fireEvent.change(screen.getByPlaceholderText('可选,关联业务单据'), { target: { value: 'BK-2' } })
    fireEvent.click(submit)
    await waitFor(() => expect(wfInstanceApi.start).toHaveBeenCalledTimes(3))
    expect(vi.mocked(wfInstanceApi.start).mock.calls[2]![0].requestId).not.toBe(first)
  })
})
