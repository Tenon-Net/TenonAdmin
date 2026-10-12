import type { WfCurrentTask, WfHisTask, WfPastTask, WfTaskParticipant } from '@/types/workflow'

export const ACTOR_STATUS = {
  waiting: 0,
  pending: 1,
  done: 2,
  skipped: 3,
} as const

export type ApprovalRound = {
  key: string
  current: boolean
  taskId?: number | string | null
  tokenId?: number | string | null
  nodeVisitId?: number | string | null
  approvalMode?: number | string | null
  createdTime?: string | null
  participants: WfTaskParticipant[]
  history: WfHisTask[]
}

function historyRoundKey(item: WfHisTask): string {
  if (item.taskId != null) return `task:${item.taskId}`
  if (item.nodeVisitId != null) return `visit:${item.tokenId ?? 'unknown'}:${item.nodeVisitId}`
  return `legacy:${item.id ?? `${item.nodeId}:${item.createTime ?? ''}`}`
}

function taskRoundKey(task: WfCurrentTask | WfPastTask): string {
  if (task.taskId != null) return `task:${task.taskId}`
  if (task.nodeVisitId != null) return `visit:${task.tokenId ?? 'unknown'}:${task.nodeVisitId}`
  return `task:unknown:${task.tokenId ?? 'unknown'}:${task.nodeId ?? 'unknown'}`
}

/** 按节点访问组织当前及历史轮次；旧记录缺少关联字段时保持独立，不猜测归属。 */
export function approvalRounds(
  nodeId: string,
  currentTasks: WfCurrentTask[],
  pastTasks: WfPastTask[],
  history: WfHisTask[],
): ApprovalRound[] {
  const rounds = new Map<string, ApprovalRound>()

  for (const task of currentTasks.filter((item) => item.nodeId === nodeId)) {
    const key = taskRoundKey(task)
    rounds.set(key, {
      key,
      current: true,
      taskId: task.taskId,
      tokenId: task.tokenId,
      nodeVisitId: task.nodeVisitId,
      approvalMode: task.approvalMode,
      createdTime: task.createdTime,
      participants: [...(task.participants ?? [])].sort((a, b) => Number(a.sort ?? 0) - Number(b.sort ?? 0)),
      history: [],
    })
  }

  for (const task of pastTasks.filter((item) => item.nodeId === nodeId)) {
    const key = taskRoundKey(task)
    if (rounds.has(key)) continue
    rounds.set(key, {
      key,
      current: false,
      taskId: task.taskId,
      tokenId: task.tokenId,
      nodeVisitId: task.nodeVisitId,
      approvalMode: task.approvalMode,
      createdTime: task.createdTime,
      participants: [...(task.participants ?? [])].sort((a, b) => Number(a.sort ?? 0) - Number(b.sort ?? 0)),
      history: [],
    })
  }

  for (const item of history.filter((entry) => entry.nodeId === nodeId)) {
    const key = historyRoundKey(item)
    const round = rounds.get(key) ?? {
      key,
      current: false,
      taskId: item.taskId,
      tokenId: item.tokenId,
      nodeVisitId: item.nodeVisitId,
      approvalMode: null,
      createdTime: null,
      participants: [],
      history: [],
    }
    round.history.push(item)
    rounds.set(key, round)
  }

  for (const round of rounds.values()) {
    round.history.sort((a, b) => (a.createTime ?? '').localeCompare(b.createTime ?? ''))
  }

  return [...rounds.values()].sort((a, b) => {
    if (a.current !== b.current) return a.current ? -1 : 1
    const aTime = [a.createdTime ?? '', ...a.history.map((item) => item.createTime ?? '')].sort().at(-1) ?? ''
    const bTime = [b.createdTime ?? '', ...b.history.map((item) => item.createTime ?? '')].sort().at(-1) ?? ''
    return bTime.localeCompare(aTime)
  })
}

export function participantHistory(round: ApprovalRound, participant: WfTaskParticipant): WfHisTask | undefined {
  const matches = round.history.filter((item) => String(item.userId) === String(participant.userId))
  return matches.at(-1)
}

export function participantGroups(participants: WfTaskParticipant[], active = true) {
  return {
    done: participants.filter((item) => Number(item.status) === ACTOR_STATUS.done),
    pending: active ? participants.filter((item) => Number(item.status) === ACTOR_STATUS.pending) : [],
    waiting: active ? participants.filter((item) => Number(item.status) === ACTOR_STATUS.waiting) : [],
    skipped: participants.filter((item) => Number(item.status) === ACTOR_STATUS.skipped
      || (!active && [ACTOR_STATUS.pending, ACTOR_STATUS.waiting].includes(Number(item.status) as 0 | 1))),
  }
}
