import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App as AntdApp } from 'antd'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import '@/locales'
import { ApiError } from '@/api'
import { createApprovalNode, createDefaultModel } from '@/workflow/model'
import type { WfId } from '@/workflow/id'
import type { WfHistoryItem, WfInstanceDetail } from '@/types/workflow'

vi.mock('@/components/AppIcon', () => ({ AppIcon: () => null }))
vi.mock('@/components/UserSelect', () => ({
  UserSelect: ({ mode, onChange }: { mode?: string; onChange?: (value: WfId | WfId[]) => void }) => (
    <button type="button" onClick={() => onChange?.(mode === 'multiple' ? [321] : 321)}>choose-users</button>
  ),
}))
vi.mock('@/api', async () => {
  class FakeApiError extends Error {
    code: number
    constructor(code: number) {
      super(`api-${code}`)
      this.code = code
    }
  }
  return {
    ApiError: FakeApiError,
    userApi: { page: vi.fn().mockResolvedValue({ items: [], total: 0 }) },
  }
})
vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
  useLocation: () => ({ pathname: '/workflow/instance/5/detail' }),
  useParams: () => ({ id: '5' }),
}))
vi.mock('@/stores/tabs', () => ({
  useTabsStore: (selector: (s: { removeTab: () => null }) => unknown) => selector({ removeTab: () => null }),
}))
vi.mock('@/stores/user', () => ({
  useUserStore: (selector: (s: { userInfo: { userId: number } }) => unknown) => selector({ userInfo: { userId: 100 } }),
}))
vi.mock('@/stores/auth', () => ({ useHasPerm: () => () => true }))
// 真 useConfirm 的 run 会吞异常并 toast;这里保留「执行 + 成败布尔」语义,不弹框。
vi.mock('@/hooks/useConfirm', () => ({
  useConfirm: () => ({
    run: async (action: () => Promise<unknown>) => {
      try {
        await action()
        return true
      } catch {
        return false
      }
    },
  }),
}))
vi.mock('@/api/workflow', () => ({
  wfInstanceApi: {
    get: vi.fn(),
    history: vi.fn().mockResolvedValue([]),
    cancel: vi.fn().mockResolvedValue({}),
    resubmit: vi.fn().mockResolvedValue({}),
  },
  wfTaskApi: {
    approve: vi.fn().mockResolvedValue({}),
    reject: vi.fn().mockResolvedValue({}),
    transfer: vi.fn().mockResolvedValue({}),
    return: vi.fn().mockResolvedValue({}),
    delegate: vi.fn().mockResolvedValue({}),
    addSign: vi.fn().mockResolvedValue({}),
    removeSign: vi.fn().mockResolvedValue({}),
    takeBack: vi.fn().mockResolvedValue({}),
    urge: vi.fn().mockResolvedValue(true),
  },
}))

import { wfInstanceApi, wfTaskApi } from '@/api/workflow'
import WfInstanceDetailPage from './detail'

const model = (() => {
  const built = createDefaultModel()
  built.root.next = createApprovalNode({ id: 'ap1', name: '经理审批' })
  return built
})()

function pendingTask(taskId: WfId, nodeName: string, nodeId = 'ap1') {
  return { taskId, nodeId, nodeName, instanceId: 5, definitionId: 9, definitionName: '请假' }
}

function detailOf(over: Partial<WfInstanceDetail> = {}): WfInstanceDetail {
  return {
    id: 5,
    definitionId: 9,
    definitionName: '请假',
    version: 1,
    status: 1,
    starterUserId: 100,
    createTime: '2026-01-01T00:00:00Z',
    visitedNodeIds: ['start'],
    currentNodeIds: ['ap1'],
    currentTasks: [],
    hisTasks: [],
    parallelForks: [],
    model,
    ...over,
  } as WfInstanceDetail
}

function mount(detail: WfInstanceDetail, loadedHistory: WfHistoryItem[] = []) {
  vi.mocked(wfInstanceApi.get).mockResolvedValue(detail)
  vi.mocked(wfInstanceApi.history).mockResolvedValue(loadedHistory)
  render(<AntdApp><WfInstanceDetailPage /></AntdApp>)
}

const okButton = () => screen.getByRole('button', { name: /确\s*定/ })

/** antd 的 confirmLoading 会吞掉点击,重试前先等确定钮退出 loading。 */
const waitOkIdle = () => waitFor(() => expect(okButton().className).not.toContain('ant-btn-loading'))

beforeEach(() => vi.clearAllMocks())
afterEach(cleanup)

describe('WfInstanceDetailPage 动词', () => {
  it('多条待办未选目标时不借用 currentNodeIds[0] 的自定义按钮文案', async () => {
    const dual = createDefaultModel()
    const armA = createApprovalNode({
      id: 'ap-a',
      name: 'A 臂',
      props: { buttonLabels: { approve: 'A臂专属同意' } },
    })
    const armB = createApprovalNode({ id: 'ap-b', name: 'B 臂' })
    dual.root.next = armA
    armA.next = armB
    mount(detailOf({
      // 前端树模型含 props:null 等联合型,OpenAPI 生成的详情 model 更宽;测试只需要结构语义。
      model: dual as WfInstanceDetail['model'],
      currentNodeIds: ['ap-a', 'ap-b'],
      myPendingTasks: [pendingTask(77, 'A 臂', 'ap-a'), pendingTask(78, 'B 臂', 'ap-b')],
    }))

    // 未选目标:工具栏必须是通用「同意」,不能串成 A 臂的自定义文案。
    expect(await screen.findByRole('button', { name: /^同\s*意$/ })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'A臂专属同意' })).toBeNull()
  })

  it('单条待办自动锁定目标 task,同意带上 taskId 与请求键', async () => {
    mount(detailOf({ myPendingTasks: [pendingTask(77, '经理审批')] }))

    fireEvent.click(await screen.findByRole('button', { name: /同\s*意/ }))
    fireEvent.click(okButton())

    await waitFor(() => expect(wfTaskApi.approve).toHaveBeenCalled())
    const body = vi.mocked(wfTaskApi.approve).mock.calls[0]![0]
    expect(body).toMatchObject({ taskId: 77, comment: null, toUserId: 0, targetNodeId: null })
    expect(typeof body.requestId).toBe('string')
  })

  it('19 位 taskId 提交时保持十进制字符串', async () => {
    const taskId = '9223372036854775807'
    mount(detailOf({ myPendingTasks: [pendingTask(taskId, '经理审批')] }))

    fireEvent.click(await screen.findByRole('button', { name: /同\s*意/ }))
    fireEvent.click(okButton())

    await waitFor(() => expect(wfTaskApi.approve).toHaveBeenCalled())
    expect(vi.mocked(wfTaskApi.approve).mock.calls[0]![0].taskId).toBe(taskId)
  })

  it('多条待办不替用户挑目标:未选就提交被挡住,不发请求', async () => {
    mount(detailOf({ myPendingTasks: [pendingTask(77, 'A 臂审批'), pendingTask(78, 'B 臂审批')] }))

    fireEvent.click(await screen.findByRole('button', { name: /同\s*意/ }))
    expect(screen.getAllByText('选择目标任务').length).toBeGreaterThan(0)
    fireEvent.click(okButton())

    await waitFor(() => expect(screen.getByText('请选择一个目标任务')).toBeTruthy())
    expect(wfTaskApi.approve).not.toHaveBeenCalled()
  })

  it('多条待办显式选择后把动作提交到目标 task', async () => {
    mount(detailOf({ myPendingTasks: [pendingTask(77, 'A 臂审批'), pendingTask(78, 'B 臂审批')] }))

    fireEvent.click(await screen.findByRole('button', { name: /同\s*意/ }))
    fireEvent.mouseDown(document.querySelector('.ant-modal .ant-select-content')!)
    fireEvent.click(await screen.findByTitle(/B 臂审批.*#78/))
    fireEvent.click(okButton())

    await waitFor(() => expect(wfTaskApi.approve).toHaveBeenCalled())
    expect(vi.mocked(wfTaskApi.approve).mock.calls[0]![0]).toMatchObject({ taskId: 78 })
  })

  it('转办未选人时被挡住,不发请求', async () => {
    mount(detailOf({ myPendingTasks: [pendingTask(77, '经理审批')] }))

    fireEvent.click(await screen.findByRole('button', { name: /转\s*办/ }))
    fireEvent.click(okButton())

    await waitFor(() => expect(screen.getByText('请选择转办对象')).toBeTruthy())
    expect(wfTaskApi.transfer).not.toHaveBeenCalled()
  })

  it('网络失败复用同一请求键重试,业务失败后换新键', async () => {
    mount(detailOf({ myPendingTasks: [pendingTask(77, '经理审批')] }))
    await screen.findByRole('button', { name: /同\s*意/ })

    // 第一次:网络层没拿到确定结果 → 保留同键
    vi.mocked(wfTaskApi.approve).mockRejectedValueOnce(new TypeError('Failed to fetch'))
    fireEvent.click(screen.getByRole('button', { name: /同\s*意/ }))
    fireEvent.click(okButton())
    await waitFor(() => expect(wfTaskApi.approve).toHaveBeenCalledTimes(1))
    await waitOkIdle()
    const first = vi.mocked(wfTaskApi.approve).mock.calls[0]![0].requestId

    // 第二次:同一个弹窗内重试,键必须一样(服务端回执才能兜住"其实已经成功了")
    vi.mocked(wfTaskApi.approve).mockRejectedValueOnce(new ApiError(48001, 'busy'))
    fireEvent.click(okButton())
    await waitFor(() => expect(wfTaskApi.approve).toHaveBeenCalledTimes(2))
    await waitOkIdle()
    expect(vi.mocked(wfTaskApi.approve).mock.calls[1]![0].requestId).toBe(first)

    // 第三次:上一轮已是业务失败(结算完成)→ 换新键
    fireEvent.click(okButton())
    await waitFor(() => expect(wfTaskApi.approve).toHaveBeenCalledTimes(3))
    expect(vi.mocked(wfTaskApi.approve).mock.calls[2]![0].requestId).not.toBe(first)
  }, 10_000)

  it('网络失败后编辑载荷会为下一次请求换键', async () => {
    mount(detailOf({ myPendingTasks: [pendingTask(77, '经理审批')] }))
    vi.mocked(wfTaskApi.approve).mockRejectedValueOnce(new TypeError('Failed to fetch'))

    fireEvent.click(await screen.findByRole('button', { name: /同\s*意/ }))
    fireEvent.click(okButton())
    await waitFor(() => expect(wfTaskApi.approve).toHaveBeenCalledTimes(1))
    await waitOkIdle()
    const first = vi.mocked(wfTaskApi.approve).mock.calls[0]![0].requestId

    fireEvent.change(screen.getByPlaceholderText('填写意见(可选)'), { target: { value: '修改后的意见' } })
    fireEvent.click(okButton())
    await waitFor(() => expect(wfTaskApi.approve).toHaveBeenCalledTimes(2))
    expect(vi.mocked(wfTaskApi.approve).mock.calls[1]![0].requestId).not.toBe(first)
  })

  it('拿回只在服务端给出 myTakeBackTaskId 时出现,并打在那条 task 上', async () => {
    mount(detailOf({ myTakeBackTaskId: 88 }))

    fireEvent.click(await screen.findByRole('button', { name: /拿\s*回/ }))
    fireEvent.click(okButton())

    await waitFor(() => expect(wfTaskApi.takeBack).toHaveBeenCalled())
    expect(vi.mocked(wfTaskApi.takeBack).mock.calls[0]![0]).toMatchObject({ taskId: 88 })
  })

  it('没有 myTakeBackTaskId 时不给拿回入口', async () => {
    mount(detailOf({}))
    await screen.findByRole('button', { name: /撤\s*销/ })
    expect(screen.queryByRole('button', { name: /拿\s*回/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /重新提交/ })).toBeNull()
  })

  it('已有同意记录后不再给撤销入口,催办仍在', async () => {
    mount(detailOf({
      hisTasks: [{ id: 1, nodeId: 'ap1', action: 1, userId: 100, createTime: '2026-01-01T01:00:00Z' }],
      currentTasks: [{ taskId: 90, tokenId: 1, nodeId: 'ap1', nodeName: '经理审批' }],
    }))

    await screen.findByRole('button', { name: /催\s*办/ })
    expect(screen.queryByRole('button', { name: /撤\s*销/ })).toBeNull()
  })

  it('催办不生成请求键(不进 receipt),只带 taskId', async () => {
    mount(detailOf({ currentTasks: [{ taskId: 90, tokenId: 1, nodeId: 'ap1', nodeName: '经理审批' }] }))

    fireEvent.click(await screen.findByRole('button', { name: /催\s*办/ }))
    fireEvent.click(okButton())

    await waitFor(() => expect(wfTaskApi.urge).toHaveBeenCalled())
    expect(vi.mocked(wfTaskApi.urge).mock.calls[0]![0]).toEqual({ taskId: 90 })
  })

  it('当前任务与催办目标标签显示 NodeVisitId', async () => {
    mount(detailOf({
      currentTasks: [
        { taskId: '9000000000000000001', tokenId: '9000000000000000002', nodeVisitId: '9000000000000000003', nodeId: 'ap1', nodeName: '经理审批' },
        { taskId: '9000000000000000004', tokenId: '9000000000000000005', nodeVisitId: '9000000000000000006', nodeId: 'ap2', nodeName: '财务审批' },
      ],
    }))

    expect(await screen.findByText(/NodeVisit #9000000000000000003/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: /催\s*办/ }))
    fireEvent.mouseDown(document.querySelector('.ant-modal .ant-select-content')!)
    expect(await screen.findByTitle(/NodeVisit #9000000000000000006/)).toBeTruthy()
  })

  it('退回事件是最新生命周期且无活动并行分叉时显示重提', async () => {
    mount(detailOf({ variablesJson: '{"days":3}' }), [
      { id: 1, eventType: 12, sequence: 3 },
      { id: 2, eventType: 14, sequence: 4 },
    ] as WfHistoryItem[])

    fireEvent.click(await screen.findByRole('button', { name: /重新提交/ }))
    fireEvent.click(okButton())

    await waitFor(() => expect(wfInstanceApi.resubmit).toHaveBeenCalled())
    expect(vi.mocked(wfInstanceApi.resubmit).mock.calls[0]![0]).toMatchObject({
      instanceId: '5',
      variablesJson: '{"days":3}',
    })
  })

  it('最近一次生命周期是已重提时不再显示重提', async () => {
    mount(detailOf(), [
      { id: 1, eventType: 14, sequence: 4 },
      { id: 2, eventType: 12, sequence: 5 },
    ] as WfHistoryItem[])
    await screen.findByRole('button', { name: /撤\s*销/ })
    expect(screen.queryByRole('button', { name: /重新提交/ })).toBeNull()
  })

  it('重提表单忽略历史 hidden/readonly 权限并提交发起人自选人员', async () => {
    const withForm = createDefaultModel()
    withForm.formSchema = {
      version: 1,
      fields: [{ key: 'title', label: '标题', type: 'text', required: true }],
    }
    withForm.root.next = createApprovalNode({
      id: 'ap1',
      name: '经理审批',
      props: {
        assignee: { provider: 'selfSelect' },
        formPerms: [{ field: 'title', access: 'hidden' }],
      },
    })
    mount(detailOf({
      model: withForm as WfInstanceDetail['model'],
      variablesJson: '{"title":"原值"}',
      visitedNodeIds: ['start', 'ap1'],
      hisTasks: [{ id: 9, nodeId: 'ap1', action: 2, userId: 100 }],
    }), [{ id: 2, eventType: 14, sequence: 4 }] as WfHistoryItem[])

    fireEvent.click(await screen.findByRole('button', { name: /重新提交/ }))
    const titleInputs = await screen.findAllByLabelText('标题')
    expect(titleInputs).toHaveLength(1)
    expect(titleInputs[0]).toHaveProperty('disabled', false)
    fireEvent.change(titleInputs[0]!, { target: { value: '重提值' } })
    fireEvent.click(screen.getByRole('button', { name: 'choose-users' }))
    fireEvent.click(okButton())

    await waitFor(() => expect(wfInstanceApi.resubmit).toHaveBeenCalled())
    expect(vi.mocked(wfInstanceApi.resubmit).mock.calls[0]![0]).toMatchObject({
      instanceId: '5',
      variablesJson: '{"title":"重提值"}',
      selectedUserIdsByNode: { ap1: [321] },
    })
  })

  it('活动中的并行分叉阻止重提', async () => {
    mount(detailOf({
      parallelForks: [{ forkId: 1, nodeId: 'pa1', status: 1, arms: [] }],
    }), [{ id: 2, eventType: 14, sequence: 4 }] as WfHistoryItem[])
    await screen.findByRole('button', { name: /撤\s*销/ })
    expect(screen.queryByRole('button', { name: /重新提交/ })).toBeNull()
  })

  it('回放非空并行分叉与各臂当前节点、状态和原因', async () => {
    mount(detailOf({
      parallelForks: [{
        forkId: 10,
        nodeId: 'pa1',
        nodeName: '并行会签',
        pendingArmCount: 1,
        status: 1,
        arms: [
          { forkId: 10, armId: 'finance', childTokenId: 21, status: 1, currentNodeId: 'ap-fin', currentNodeName: '财务审批' },
          { forkId: 10, armId: 'legal', childTokenId: 22, status: 3, reason: '条件不满足' },
        ],
      }],
    }))

    expect(await screen.findByText('并行会签')).toBeTruthy()
    expect(screen.getByText(/财务审批.*#21/)).toBeTruthy()
    expect(screen.getByText('条件不满足')).toBeTruthy()
    expect(screen.getByText('finance')).toBeTruthy()
    expect(screen.getByText('legal')).toBeTruthy()
  })
})
