import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { App as AntdApp } from 'antd'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import '@/locales'
import { createApprovalNode, createDefaultModel, createParallelNode } from '@/workflow/model'
import type { WfModel } from '@/workflow/schema'

const testState = vi.hoisted(() => ({
  query: 'id=7',
  has: vi.fn<(code: string) => boolean>(() => true),
}))

vi.mock('@/components/AppIcon', () => ({ AppIcon: () => null }))
vi.mock('@/api', () => ({
  userApi: { page: vi.fn().mockResolvedValue({ items: [], total: 0 }) },
  roleApi: { page: vi.fn().mockResolvedValue({ items: [], total: 0 }) },
  positionApi: { page: vi.fn().mockResolvedValue({ items: [], total: 0 }) },
  orgApi: { list: vi.fn().mockResolvedValue([]) },
}))
vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
  useSearchParams: () => [new URLSearchParams(testState.query)],
}))
vi.mock('@/stores/auth', () => ({ useHasPerm: () => testState.has }))
vi.mock('@/hooks/useConfirm', () => ({ useConfirm: () => ({ run: vi.fn().mockResolvedValue(true) }) }))
vi.mock('@/api/workflow', () => ({
  wfDefinitionApi: { get: vi.fn(), update: vi.fn().mockResolvedValue(true), publish: vi.fn().mockResolvedValue(true), add: vi.fn() },
}))

import { wfDefinitionApi } from '@/api/workflow'
import WfDesignerPage from './designer'

/** 只有一条空并行臂的模型:validateModel 必然报错(并行至少两臂),用来钉住保存闸门。 */
function invalidModel(): WfModel {
  const model = createDefaultModel()
  model.root.next = createParallelNode({ id: 'pa1', parallelArms: [{ id: 'arm1', name: 'A', next: null }] })
  return model
}

function validModel(): WfModel {
  const model = createDefaultModel()
  model.root.next = createApprovalNode({ id: 'ap1', name: '经理审批' })
  return model
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

function mount(model: WfModel) {
  vi.mocked(wfDefinitionApi.get).mockResolvedValue({ id: 7, name: '请假', groupName: 'HR', status: 0, model } as never)
  return render(<AntdApp><WfDesignerPage /></AntdApp>)
}

beforeEach(() => {
  vi.clearAllMocks()
  testState.query = 'id=7'
  testState.has.mockReturnValue(true)
})
afterEach(cleanup)

describe('WfDesignerPage', () => {
  it('加载定义后渲染节点树与缩放工具条', async () => {
    mount(validModel())
    expect(await screen.findByText('经理审批')).toBeTruthy()
    expect(screen.getByRole('toolbar', { name: '画布缩放' })).toBeTruthy()
    expect(screen.getByText('100%')).toBeTruthy()
  })

  it('从详情切到无 id 新建态时清除缓存中的旧定义', async () => {
    const view = mount(validModel())
    expect(await screen.findByText('经理审批')).toBeTruthy()

    testState.query = ''
    view.rerender(<AntdApp><WfDesignerPage /></AntdApp>)

    expect(await screen.findByText(/尚未打开流程/)).toBeTruthy()
    expect(screen.queryByText('经理审批')).toBeNull()
    expect((screen.getByPlaceholderText('流程名称') as HTMLInputElement).value).toBe('')
  })

  it('缩放按步长增减并在边界禁用', async () => {
    mount(validModel())
    await screen.findByText('经理审批')
    fireEvent.click(screen.getByRole('button', { name: '缩小' }))
    expect(screen.getByText('90%')).toBeTruthy()
    for (let i = 0; i < 5; i += 1) fireEvent.click(screen.getByRole('button', { name: '缩小' }))
    expect(screen.getByText('50%')).toBeTruthy()
    expect(screen.getByRole('button', { name: '缩小' })).toHaveProperty('disabled', true)
  }, 10_000)

  it('结构非法时保存被挡住,不发更新请求', async () => {
    mount(invalidModel())
    await waitFor(() => expect(wfDefinitionApi.get).toHaveBeenCalled())

    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))
    await waitFor(() => expect(screen.getAllByText('流程结构不合法,请检查节点').length).toBeGreaterThan(0))
    expect(wfDefinitionApi.update).not.toHaveBeenCalled()
  })

  it('结构合法时保存带上名称、分组与模型', async () => {
    mount(validModel())
    await screen.findByText('经理审批')

    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }))
    await waitFor(() => expect(wfDefinitionApi.update).toHaveBeenCalled())
    expect(vi.mocked(wfDefinitionApi.update).mock.calls[0]![0]).toMatchObject({ id: 7, name: '请假', groupName: 'HR' })
  })

  it('缺少定义写权限时隐藏保存和空态新建控件', async () => {
    testState.has.mockReturnValue(false)
    mount(validModel())
    await screen.findByText('经理审批')
    expect(screen.queryByRole('button', { name: /保\s*存/ })).toBeNull()

    cleanup()
    testState.query = ''
    render(<AntdApp><WfDesignerPage /></AntdApp>)
    expect(await screen.findByText(/尚未打开流程/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: /创\s*建/ })).toBeNull()
    expect(screen.queryByPlaceholderText('流程名称')).toBeNull()
  })

  it('仅有发布权限但没有更新权限时隐藏发布按钮', async () => {
    testState.has.mockImplementation((code) =>
      code === 'GET:/api/v1/workflow/definition/{id}'
      || code === 'POST:/api/v1/workflow/definition/publish')
    mount(validModel())

    await screen.findByText('经理审批')
    expect(screen.queryByRole('button', { name: /发\s*布/ })).toBeNull()
  })

  it('较晚返回的旧定义不能覆盖当前定义', async () => {
    const first = deferred<Awaited<ReturnType<typeof wfDefinitionApi.get>>>()
    const second = deferred<Awaited<ReturnType<typeof wfDefinitionApi.get>>>()
    vi.mocked(wfDefinitionApi.get)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const view = render(<AntdApp><WfDesignerPage /></AntdApp>)
    await waitFor(() => expect(wfDefinitionApi.get).toHaveBeenCalledWith('7'))
    testState.query = 'id=8'
    view.rerender(<AntdApp><WfDesignerPage /></AntdApp>)
    await waitFor(() => expect(wfDefinitionApi.get).toHaveBeenCalledWith('8'))

    second.resolve({ id: '8', name: '定义 B', status: 0, model: validModel() } as never)
    expect(await screen.findByDisplayValue('定义 B')).toBeTruthy()
    first.resolve({ id: '7', name: '定义 A', status: 0, model: invalidModel() } as never)
    await Promise.resolve()
    expect(screen.queryByDisplayValue('定义 A')).toBeNull()
    expect(screen.getByDisplayValue('定义 B')).toBeTruthy()
  })
})
