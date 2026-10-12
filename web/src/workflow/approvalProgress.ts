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

function idKey(value: number | string | null | undefined): string {
  return value == null ? '' : String(value)
}

function visitRoundKey(
  nodeId: string | null | undefined,
  tokenId: number | string | null | undefined,
  nodeVisitId: number | string | null | undefined,
): string | null {
  if (!nodeId || tokenId == null || nodeVisitId == null) return null
  return `visit:${nodeId}:${idKey(tokenId)}:${idKey(nodeVisitId)}`
}

function historyRoundKey(item: WfHisTask): string {
  if (item.taskId != null) return `task:${idKey(item.taskId)}`
  const visitKey = visitRoundKey(item.nodeId, item.tokenId, item.nodeVisitId)
  if (visitKey) return visitKey
  return `legacy:${item.id ?? `${item.nodeId}:${item.createTime ?? ''}`}`
}

function taskRoundKey(task: WfCurrentTask | WfPastTask): string {
  if (task.taskId != null) return `task:${idKey(task.taskId)}`
  const visitKey = visitRoundKey(task.nodeId, task.tokenId, task.nodeVisitId)
  if (visitKey) return visitKey
  return `task:unknown:${task.tokenId ?? 'unknown'}:${task.nodeId ?? 'unknown'}`
}

/** 按节点访问组织当前及历史轮次；旧记录仅在完整访问标识唯一匹配时归并，否则保持独立。 */
export function approvalRounds(
  nodeId: string,
  currentTasks: WfCurrentTask[],
  pastTasks: WfPastTask[],
  history: WfHisTask[],
): ApprovalRound[] {
  const rounds = new Map<string, ApprovalRound>()
  const roundsByVisit = new Map<string, ApprovalRound[]>()

  const indexVisit = (task: WfCurrentTask | WfPastTask, round: ApprovalRound) => {
    const visitKey = visitRoundKey(task.nodeId, task.tokenId, task.nodeVisitId)
    if (!visitKey) return
    const candidates = roundsByVisit.get(visitKey) ?? []
    if (!candidates.includes(round)) candidates.push(round)
    roundsByVisit.set(visitKey, candidates)
  }

  for (const task of currentTasks.filter((item) => item.nodeId === nodeId)) {
    const key = taskRoundKey(task)
    const round: ApprovalRound = {
      key,
      current: true,
      taskId: task.taskId,
      tokenId: task.tokenId,
      nodeVisitId: task.nodeVisitId,
      approvalMode: task.approvalMode,
      createdTime: task.createdTime,
      participants: [...(task.participants ?? [])].sort((a, b) => Number(a.sort ?? 0) - Number(b.sort ?? 0)),
      history: [],
    }
    rounds.set(key, round)
    indexVisit(task, round)
  }

  for (const task of pastTasks.filter((item) => item.nodeId === nodeId)) {
    const key = taskRoundKey(task)
    const existing = rounds.get(key)
    if (existing) {
      indexVisit(task, existing)
      continue
    }
    const round: ApprovalRound = {
      key,
      current: false,
      taskId: task.taskId,
      tokenId: task.tokenId,
      nodeVisitId: task.nodeVisitId,
      approvalMode: task.approvalMode,
      createdTime: task.createdTime,
      participants: [...(task.participants ?? [])].sort((a, b) => Number(a.sort ?? 0) - Number(b.sort ?? 0)),
      history: [],
    }
    rounds.set(key, round)
    indexVisit(task, round)
  }

  for (const item of history.filter((entry) => entry.nodeId === nodeId)) {
    const key = historyRoundKey(item)
    const visitCandidates = item.taskId == null ? roundsByVisit.get(key) ?? [] : []
    const matchedRound = rounds.get(key) ?? (visitCandidates.length === 1 ? visitCandidates[0] : undefined)
    const round = matchedRound ?? {
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
    if (!matchedRound) rounds.set(key, round)
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
