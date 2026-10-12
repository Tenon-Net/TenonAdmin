import { describe, expect, it } from 'vitest'
import { approvalRounds, participantGroups, participantHistory } from './approvalProgress'

describe('workflow approval progress', () => {
  it('keeps current and previous visits separate for the same node', () => {
    const rounds = approvalRounds('approval', [
      {
        taskId: 20,
        tokenId: 2,
        nodeVisitId: 200,
        nodeId: 'approval',
        participants: [{ actorId: 1, userId: 2, status: 1, sort: 1 }],
      },
    ], [], [
      { id: 1, taskId: 10, tokenId: 1, nodeVisitId: 100, nodeId: 'approval', userId: 1, action: 1 },
      { id: 2, taskId: 20, tokenId: 2, nodeVisitId: 200, nodeId: 'approval', userId: 3, action: 4 },
    ])

    expect(rounds).toHaveLength(2)
    expect(rounds[0]).toMatchObject({ current: true, taskId: 20 })
    expect(rounds[0]?.history[0]?.action).toBe(4)
    expect(rounds[1]).toMatchObject({ current: false, taskId: 10 })
  })

  it('does not merge legacy history with no task or visit identity', () => {
    const rounds = approvalRounds('approval', [], [], [
      { id: 1, nodeId: 'approval', userId: 1, action: 1 },
      { id: 2, nodeId: 'approval', userId: 2, action: 2 },
    ])
    expect(rounds.map((item) => item.key)).toEqual(['legacy:1', 'legacy:2'])
  })

  it('keeps multiple tasks in one visit separate', () => {
    const rounds = approvalRounds('approval', [], [
      { taskId: 10, tokenId: 1, nodeVisitId: 100, nodeId: 'approval', participants: [] },
      { taskId: 11, tokenId: 1, nodeVisitId: 100, nodeId: 'approval', participants: [] },
    ], [])
    expect(rounds.map((item) => item.key)).toEqual(['task:10', 'task:11'])
  })

  it.each(['current', 'past'] as const)('merges taskless history into the unique %s task visit', (source) => {
    const task = {
      taskId: '7',
      tokenId: '2',
      nodeVisitId: '70',
      nodeId: 'approval',
      participants: [],
    }
    const currentTasks = source === 'current' ? [task] : []
    const pastTasks = source === 'past' ? [task] : []
    const rounds = approvalRounds('approval', currentTasks, pastTasks, [
      { id: 1, taskId: null, tokenId: 2, nodeVisitId: 70, nodeId: 'approval', userId: 1, action: 1 },
    ])

    expect(rounds).toHaveLength(1)
    expect(rounds[0]).toMatchObject({ taskId: '7', current: source === 'current' })
    expect(rounds[0]?.history.map((item) => item.id)).toEqual([1])
  })

  it('does not choose between multiple tasks with the same visit identity', () => {
    const rounds = approvalRounds('approval', [], [
      { taskId: 10, tokenId: 1, nodeVisitId: 100, nodeId: 'approval', participants: [] },
      { taskId: 11, tokenId: 1, nodeVisitId: 100, nodeId: 'approval', participants: [] },
    ], [
      { id: 1, taskId: null, tokenId: 1, nodeVisitId: 100, nodeId: 'approval', userId: 1, action: 1 },
    ])

    expect(rounds).toHaveLength(3)
    expect(rounds.find((item) => item.key === 'visit:approval:1:100')?.history).toHaveLength(1)
    expect(rounds.filter((item) => item.taskId != null).every((item) => item.history.length === 0)).toBe(true)
  })

  it.each([
    { tokenId: null, nodeVisitId: 100 },
    { tokenId: 1, nodeVisitId: null },
  ])('keeps history independent when visit identity is incomplete: %o', ({ tokenId, nodeVisitId }) => {
    const rounds = approvalRounds('approval', [], [
      { taskId: 10, tokenId: 1, nodeVisitId: 100, nodeId: 'approval', participants: [] },
    ], [
      { id: 1, taskId: null, tokenId, nodeVisitId, nodeId: 'approval', userId: 1, action: 1 },
    ])

    expect(rounds).toHaveLength(2)
    expect(rounds.find((item) => item.taskId === 10)?.history).toEqual([])
    expect(rounds.find((item) => item.key === 'legacy:1')?.history).toHaveLength(1)
  })

  it('does not merge taskless history across a different token or node', () => {
    const rounds = approvalRounds('approval', [], [
      { taskId: 10, tokenId: 1, nodeVisitId: 100, nodeId: 'approval', participants: [] },
      { taskId: 11, tokenId: 2, nodeVisitId: 100, nodeId: 'other', participants: [] },
    ], [
      { id: 1, taskId: null, tokenId: 2, nodeVisitId: 100, nodeId: 'approval', userId: 1, action: 1 },
      { id: 2, taskId: null, tokenId: 1, nodeVisitId: 100, nodeId: 'other', userId: 2, action: 1 },
    ])

    expect(rounds).toHaveLength(2)
    expect(rounds.find((item) => item.taskId === 10)?.history).toEqual([])
    expect(rounds.find((item) => item.key === 'visit:approval:2:100')?.history).toHaveLength(1)
  })

  it('matches numeric and string task ids as the same round', () => {
    const rounds = approvalRounds('approval', [], [
      { taskId: '10', tokenId: 1, nodeVisitId: 100, nodeId: 'approval', participants: [] },
    ], [
      { id: 1, taskId: 10, tokenId: 1, nodeVisitId: 100, nodeId: 'approval', userId: 1, action: 1 },
    ])

    expect(rounds).toHaveLength(1)
    expect(rounds[0]?.history).toHaveLength(1)
  })

  it('uses task creation time to keep a newer actionless cancelled round before an older approved round', () => {
    const rounds = approvalRounds('approval', [], [
      {
        taskId: 10,
        nodeId: 'approval',
        createdTime: '2026-09-01T10:00:00',
        participants: [{ actorId: 1, userId: 1, status: 2, sort: 1 }],
      },
      {
        taskId: 20,
        nodeId: 'approval',
        createdTime: '2026-09-02T10:00:00',
        participants: [{ actorId: 2, userId: 2, status: 3, sort: 1 }],
      },
    ], [
      { id: 1, taskId: 10, nodeId: 'approval', userId: 1, action: 1, createTime: '2026-09-01T10:05:00' },
    ])

    expect(rounds.map((item) => item.taskId)).toEqual([20, 10])
  })

  it('keeps done distinct from the approval action', () => {
    const groups = participantGroups([
      { actorId: 1, userId: 1, status: 2, sort: 1 },
      { actorId: 2, userId: 2, status: 1, sort: 2 },
      { actorId: 3, userId: 3, status: 0, sort: 3 },
      { actorId: 4, userId: 4, status: 3, sort: 4 },
    ])
    expect(groups.done.map((item) => item.userId)).toEqual([1])
    expect(groups.pending.map((item) => item.userId)).toEqual([2])
    expect(groups.waiting.map((item) => item.userId)).toEqual([3])
    expect(groups.skipped.map((item) => item.userId)).toEqual([4])
  })

  it('treats stale pending and waiting actors from a historical task as no longer actionable', () => {
    const groups = participantGroups([
      { actorId: 1, userId: 1, status: 1, sort: 1 },
      { actorId: 2, userId: 2, status: 0, sort: 2 },
    ], false)
    expect(groups.pending).toEqual([])
    expect(groups.waiting).toEqual([])
    expect(groups.skipped.map((item) => item.userId)).toEqual([1, 2])
  })

  it('keeps a historical action available when the engine closed that actor as skipped', () => {
    const round = approvalRounds('approval', [], [{
      taskId: 10,
      nodeId: 'approval',
      participants: [{ actorId: 1, userId: 1, status: 3, sort: 1 }],
    }], [
      { id: 1, taskId: 10, nodeId: 'approval', userId: 1, action: 1 },
    ])[0]!
    expect(participantHistory(round, round.participants[0]!)?.action).toBe(1)
  })
})
