import { describe, expect, it } from 'vitest'
import type { WfCurrentTask, WfHisTask } from '@/types/workflow'
import { approvalCardGroups, buildApprovalProgress, participantHistory } from './approvalProgress'

function task(over: Partial<WfCurrentTask>): WfCurrentTask {
  return {
    taskId: 1,
    tokenId: 10,
    nodeVisitId: 100,
    nodeId: 'approve',
    nodeName: '部门审批',
    approvalMode: 'any',
    participants: [],
    ...over,
  }
}

describe('approvalProgress', () => {
  it('或签完成后保留明确动作并把跳过人员归为无需处理', () => {
    const past = task({
      participants: [
        { actorId: 11, userId: 101, displayName: '张三', status: 2, sort: 0 },
        { actorId: 12, userId: 102, displayName: '李四', status: 3, sort: 1 },
      ],
    })
    const history = [{
      id: 201,
      taskId: 1,
      tokenId: 10,
      nodeVisitId: 100,
      nodeId: 'approve',
      nodeName: '部门审批',
      userId: 101,
      operatorName: '张三',
      isAutomatic: true,
      action: 1,
      createTime: '2026-10-12T10:00:00Z',
    }] as WfHisTask[]

    const progress = buildApprovalProgress([], [past], history).get('approve')!
    expect(approvalCardGroups(progress)).toEqual([
      { key: 'handled', items: [{ name: '张三', action: 'approve', automatic: true }] },
      { key: 'skipped', items: [{ name: '李四', action: undefined }] },
    ])
  })

  it('退回收口后按实际动作区分已处理与未行动人员', () => {
    const past = task({
      taskId: 3,
      participants: [
        { actorId: 31, userId: 201, displayName: '张三', status: 3, sort: 0 },
        { actorId: 32, userId: 202, displayName: '李四', status: 3, sort: 1 },
        { actorId: 33, userId: 203, displayName: '王五', status: 3, sort: 2 },
      ],
    })
    const history = [
      { id: 301, taskId: 3, nodeId: 'approve', userId: 201, action: 1, createTime: '2026-10-12T09:00:00Z' },
      { id: 302, taskId: 3, nodeId: 'approve', userId: 202, action: 4, createTime: '2026-10-12T10:00:00Z' },
    ] as WfHisTask[]

    const progress = buildApprovalProgress([], [past], history).get('approve')!
    expect(approvalCardGroups(progress)).toEqual([
      { key: 'handled', items: [{ name: '张三', action: 'approve' }, { name: '李四', action: 'return' }] },
      { key: 'skipped', items: [{ name: '王五', action: undefined }] },
    ])
  })

  it('历史轮次按事实时间排序，不依赖接口数组顺序', () => {
    const later = task({ taskId: 2, nodeVisitId: 102, participants: [] })
    const earlier = task({ taskId: 1, nodeVisitId: 101, participants: [] })
    const history = [
      { id: 2, taskId: 2, tokenId: 10, nodeVisitId: 102, nodeId: 'approve', userId: 102, action: 2, createTime: '2026-10-12T11:00:00Z' },
      { id: 1, taskId: 1, tokenId: 10, nodeVisitId: 101, nodeId: 'approve', userId: 101, action: 1, createTime: '2026-10-12T09:00:00Z' },
    ] as WfHisTask[]

    const progress = buildApprovalProgress([], [later, earlier], history).get('approve')!
    expect(progress.pastRounds.map((round) => round.task?.taskId)).toEqual([1, 2])
    expect(approvalCardGroups(progress)[0]?.items[0]?.action).toBe('reject')
  })

  it('无人动作的新取消轮按任务创建时间排在旧审批轮之后', () => {
    const cancelled = task({
      taskId: 2,
      createdTime: '2026-10-12T11:00:00Z',
      participants: [{ actorId: 2, userId: 102, displayName: '李四', status: 3, sort: 0 }],
    })
    const approved = task({
      taskId: 1,
      createdTime: '2026-10-12T08:00:00Z',
      participants: [{ actorId: 1, userId: 101, displayName: '张三', status: 3, sort: 0 }],
    })
    const history = [{
      id: 1,
      taskId: 1,
      nodeId: 'approve',
      userId: 101,
      action: 1,
      createTime: '2026-10-12T09:00:00Z',
    }] as WfHisTask[]

    const progress = buildApprovalProgress([], [cancelled, approved], history).get('approve')!
    expect(progress.pastRounds.map((round) => round.task?.taskId)).toEqual([1, 2])
    expect(approvalCardGroups(progress)).toEqual([
      { key: 'skipped', items: [{ name: '李四', action: undefined }] },
    ])
  })

  it.each([
    [3, 'transfer'],
    [6, 'delegate'],
  ] as const)('当前轮原办理人已执行 %s 时显示实际动作', (action, normalized) => {
    const current = task({
      participants: [
        { actorId: 11, userId: 101, displayName: '张三', status: 3, sort: 0 },
        { actorId: 12, userId: 102, displayName: '李四', status: 1, sort: 1 },
        { actorId: 13, userId: 103, displayName: '王五', status: 0, sort: 2 },
      ],
    })
    const history = [{
      id: 201,
      taskId: 1,
      tokenId: 10,
      nodeVisitId: 100,
      nodeId: 'approve',
      userId: 101,
      action,
    }] as WfHisTask[]

    const progress = buildApprovalProgress([current], [], history).get('approve')!
    expect(approvalCardGroups(progress)).toEqual([
      { key: 'handled', items: [{ name: '张三', action: normalized }] },
      { key: 'pending', items: [{ name: '李四', action: undefined }] },
      { key: 'waiting', items: [{ name: '王五', action: undefined }] },
    ])
  })

  it.each([
    [1, 3, 'pending'],
    [0, 6, 'waiting'],
  ] as const)('当前轮状态 %s 优先于同用户已有动作', (status, action, expected) => {
    const current = task({
      participants: [{ actorId: 11, userId: 101, displayName: '张三', status, sort: 0 }],
    })
    const history = [{
      id: 201,
      taskId: 1,
      tokenId: 10,
      nodeVisitId: 100,
      nodeId: 'approve',
      userId: 101,
      action,
    }] as WfHisTask[]

    const progress = buildApprovalProgress([current], [], history).get('approve')!
    expect(approvalCardGroups(progress)).toEqual([
      { key: expected, items: [{ name: '张三', action: undefined }] },
    ])
  })

  it('旧历史缺失任务和访问标识时逐条保留，不猜测为同一轮', () => {
    const history = [
      { id: 1, nodeId: 'approve', userId: 101, action: 1 },
      { id: 2, nodeId: 'approve', userId: 102, action: 1 },
    ] as WfHisTask[]

    const progress = buildApprovalProgress([], [], history).get('approve')!
    expect(progress.pastRounds).toHaveLength(2)
  })

  it('历史缺 TaskId 时按节点、Token 与访问标识关联保留任务', () => {
    const past = task({ taskId: 7, tokenId: 2, nodeVisitId: 70 })
    const history = [{
      id: 1,
      taskId: null,
      tokenId: 2,
      nodeVisitId: 70,
      nodeId: 'approve',
      userId: 101,
      action: 1,
    }] as WfHisTask[]

    const progress = buildApprovalProgress([], [past], history).get('approve')!
    expect(progress.pastRounds).toHaveLength(1)
    expect(progress.pastRounds[0]?.history).toHaveLength(1)
  })

  it('同一用户重新加入并同意后，不把新动作套到旧的跳过分配', () => {
    const past = task({
      participants: [
        { actorId: 1, userId: 101, displayName: '张三', status: 3, sort: 0 },
        { actorId: 2, userId: 102, displayName: '李四', status: 3, sort: 1 },
        { actorId: 3, userId: 101, displayName: '张三', status: 2, sort: 2 },
      ],
    })
    const history = [
      { id: 1, taskId: 1, nodeId: 'approve', userId: 101, actorId: 1, action: 3, comment: '转给李四', createTime: '2026-10-12T09:00:00Z' },
      { id: 2, taskId: 1, nodeId: 'approve', userId: 101, actorId: 3, action: 1, comment: '再次同意', createTime: '2026-10-12T11:00:00Z' },
    ] as WfHisTask[]

    const progress = buildApprovalProgress([], [past], history).get('approve')!
    const round = progress.pastRounds[0]!
    expect(participantHistory(past.participants![0]!, round)).toMatchObject({ action: 3, comment: '转给李四', createTime: '2026-10-12T09:00:00Z' })
    expect(participantHistory(past.participants![2]!, round)).toMatchObject({ action: 1, comment: '再次同意', createTime: '2026-10-12T11:00:00Z' })
    expect(approvalCardGroups(progress)).toEqual([
      { key: 'handled', items: [{ name: '张三', action: 'transfer' }, { name: '张三', action: 'approve' }] },
      { key: 'skipped', items: [{ name: '李四', action: undefined }] },
    ])
  })

  it('减签后重新加签再审批时，跳过的旧分配不显示后来的同意', () => {
    const past = task({
      participants: [
        { actorId: 1, userId: 101, displayName: '张三', status: 3, sort: 0 },
        { actorId: 2, userId: 102, displayName: '李四', status: 2, sort: 1 },
        { actorId: 3, userId: 101, displayName: '张三', status: 2, sort: 2 },
      ],
    })
    const history = [
      { id: 1, taskId: 1, nodeId: 'approve', userId: 102, actorId: 2, action: 9, comment: '减签', createTime: '2026-10-12T09:00:00Z' },
      { id: 2, taskId: 1, nodeId: 'approve', userId: 101, actorId: 3, action: 1, comment: '补同意', createTime: '2026-10-12T11:00:00Z' },
    ] as WfHisTask[]

    const progress = buildApprovalProgress([], [past], history).get('approve')!
    const round = progress.pastRounds[0]!
    expect(participantHistory(past.participants![0]!, round)).toBeUndefined()
    expect(participantHistory(past.participants![2]!, round)?.comment).toBe('补同意')
    expect(round.history.map((item) => item.comment)).toEqual(['减签', '补同意'])
  })

  it('重新加入且尚未处理时保持待办，不继承上一次动作', () => {
    const current = task({
      participants: [
        { actorId: 1, userId: 101, displayName: '张三', status: 3, sort: 0 },
        { actorId: 3, userId: 101, displayName: '张三', status: 1, sort: 1 },
      ],
    })
    const history = [
      { id: 1, taskId: 1, nodeId: 'approve', userId: 102, actorId: 2, action: 9, comment: '减掉', createTime: '2026-10-12T09:00:00Z' },
    ] as WfHisTask[]

    const progress = buildApprovalProgress([current], [], history).get('approve')!
    const round = progress.currentRounds[0]!
    expect(participantHistory(current.participants![1]!, round)).toBeUndefined()
    expect(approvalCardGroups(progress)).toEqual([
      { key: 'handled', items: [{ name: '#102', action: 'removeSign' }] },
      { key: 'pending', items: [{ name: '张三', action: undefined }] },
      { key: 'skipped', items: [{ name: '张三', action: undefined }] },
    ])
  })

  it('旧历史缺少分配身份时不猜测归属，也不丢动作', () => {
    const past = task({
      participants: [
        { actorId: 1, userId: 101, displayName: '张三', status: 3, sort: 0 },
        { actorId: 3, userId: 101, displayName: '张三', status: 2, sort: 1 },
      ],
    })
    const history = [
      { id: 1, taskId: 1, nodeId: 'approve', userId: 101, operatorName: '张三', action: 3, comment: '旧转办', createTime: '2026-10-12T09:00:00Z' },
      { id: 2, taskId: 1, nodeId: 'approve', userId: 101, operatorName: '张三', action: 1, comment: '旧同意', createTime: '2026-10-12T11:00:00Z' },
    ] as WfHisTask[]

    const progress = buildApprovalProgress([], [past], history).get('approve')!
    const round = progress.pastRounds[0]!
    expect(participantHistory(past.participants![0]!, round)).toBeUndefined()
    expect(participantHistory(past.participants![1]!, round)).toBeUndefined()
    expect(approvalCardGroups(progress)).toEqual([
      {
        key: 'handled',
        items: [
          { name: '张三', action: undefined },
          { name: '张三', action: 'transfer' },
          { name: '张三', action: 'approve' },
        ],
      },
      { key: 'skipped', items: [{ name: '张三', action: undefined }] },
    ])
  })
})
