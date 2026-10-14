import type { WfCurrentTask, WfHisTask, WfPastTask } from '@/types/workflow'
import { normalizeTaskAction } from '@/workflow/statusLabels'

export type WfTaskParticipant = NonNullable<WfCurrentTask['participants']>[number]
export type WfApprovalTask = WfCurrentTask | WfPastTask

export interface WfApprovalRound {
  key: string
  current: boolean
  task?: WfApprovalTask
  history: WfHisTask[]
}

export interface WfApprovalNodeProgress {
  nodeId: string
  nodeName?: string | null
  currentRounds: WfApprovalRound[]
  pastRounds: WfApprovalRound[]
}

const ACTOR_STATUS = { waiting: 0, pending: 1, done: 2, skipped: 3 } as const

function idKey(value: number | string | null | undefined): string {
  return value == null ? '' : String(value)
}

function taskRoundKey(task: WfApprovalTask): string {
  return `task:${idKey(task.taskId)}`
}

function visitRoundKey(
  nodeId: string | null | undefined,
  nodeVisitId: number | string | null | undefined,
  tokenId: number | string | null | undefined,
): string | null {
  return nodeVisitId == null ? null : `visit:${nodeId ?? ''}:${idKey(nodeVisitId)}:${idKey(tokenId)}`
}

function historyRoundKey(item: WfHisTask): string {
  if (item.taskId != null) return `task:${idKey(item.taskId)}`
  const visitKey = visitRoundKey(item.nodeId, item.nodeVisitId, item.tokenId)
  if (visitKey) return visitKey
  // 旧数据无任务/访问标识时逐条保留，避免把两轮审批错误合并。
  return `history:${idKey(item.id)}`
}

function compareHistory(a: WfHisTask, b: WfHisTask): number {
  return String(a.createTime ?? '').localeCompare(String(b.createTime ?? ''))
    || idKey(a.id).localeCompare(idKey(b.id))
}

function roundTime(round: WfApprovalRound): string {
  const historyTime = round.history.at(-1)?.createTime ?? ''
  const taskTime = round.task?.createdTime ?? ''
  const participantTime = round.task?.participants
    ?.map((item) => item.activatedTime ?? '')
    .sort()
    .at(-1) ?? ''
  return [historyTime, taskTime, participantTime].sort().at(-1) ?? ''
}

/**
 * 以任务身份组织当前轮与历史轮。pastTasks 只用于回放，绝不提升成当前待办。
 * 缺失 TaskId/NodeVisitId 的旧历史逐条成轮，保守展示可证实事实。
 */
export function buildApprovalProgress(
  currentTasks: readonly WfCurrentTask[],
  pastTasks: readonly WfPastTask[],
  hisTasks: readonly WfHisTask[],
): Map<string, WfApprovalNodeProgress> {
  const byNode = new Map<string, WfApprovalNodeProgress>()
  const rounds = new Map<string, WfApprovalRound>()

  const ensureNode = (nodeId: string, nodeName?: string | null) => {
    const found = byNode.get(nodeId)
    if (found) {
      if (!found.nodeName && nodeName) found.nodeName = nodeName
      return found
    }
    const created: WfApprovalNodeProgress = { nodeId, nodeName, currentRounds: [], pastRounds: [] }
    byNode.set(nodeId, created)
    return created
  }

  const addTask = (task: WfApprovalTask, current: boolean) => {
    if (!task.nodeId) return
    const round: WfApprovalRound = { key: taskRoundKey(task), current, task, history: [] }
    const node = ensureNode(task.nodeId, task.nodeName)
    ;(current ? node.currentRounds : node.pastRounds).push(round)
    rounds.set(round.key, round)
    const visitKey = visitRoundKey(task.nodeId, task.nodeVisitId, task.tokenId)
    if (visitKey) rounds.set(visitKey, round)
  }

  currentTasks.forEach((task) => addTask(task, true))
  pastTasks.forEach((task) => addTask(task, false))

  ;[...hisTasks].sort(compareHistory).forEach((item) => {
    if (!item.nodeId) return
    const key = historyRoundKey(item)
    const known = rounds.get(key)
    if (known) {
      known.history.push(item)
      return
    }
    const node = ensureNode(item.nodeId, item.nodeName)
    const existing = node.pastRounds.find((round) => round.key === key)
    if (existing) existing.history.push(item)
    else node.pastRounds.push({ key, current: false, history: [item] })
  })

  byNode.forEach((node) => {
    node.pastRounds.sort((a, b) => roundTime(a).localeCompare(roundTime(b)) || a.key.localeCompare(b.key))
  })

  return byNode
}

function sameId(left: number | string | null | undefined, right: number | string | null | undefined): boolean {
  return left != null && left !== '' && right != null && right !== '' && String(left) === String(right)
}

function latestHistory(items: WfHisTask[]): WfHisTask | undefined {
  return [...items].sort(compareHistory).at(-1)
}

/**
 * 同一用户可以有多条分配。有分配身份时只认这一条；旧历史没有分配身份时，
 * 仅在该用户于本轮唯一时才关联，避免把后一次同意套到先前跳过的记录上。
 */
export function participantHistory(
  participant: WfTaskParticipant,
  round: WfApprovalRound,
): WfHisTask | undefined {
  const linked = round.history.filter((item) => sameId(item.actorId, participant.actorId))
  if (linked.length) return latestHistory(linked)
  if (round.history.some((item) => item.actorId != null) && participant.actorId != null) return undefined

  const participants = round.task?.participants ?? []
  const userId = idKey(participant.userId)
  const sameUser = participants.filter((item) => idKey(item.userId) === userId)
  const unlinked = round.history.filter((item) => item.actorId == null && idKey(item.userId) === userId)
  if (sameUser.length === 1 && unlinked.length === 1) return unlinked[0]
  return undefined
}

export function participantState(participant: WfTaskParticipant, round: WfApprovalRound): {
  key: 'waiting' | 'pending' | 'handled' | 'skipped'
  action?: WfHisTask['action']
} {
  const history = participantHistory(participant, round)
  const status = Number(participant.status)
  // 转办/委托后原 actor 会在当前任务中变为 Skipped；同任务已有实际动作时仍展示动作事实。
  // 当前 Pending/Waiting 代表真实待办状态，优先级高于历史动作。
  if (round.current && status === ACTOR_STATUS.waiting) return { key: 'waiting' }
  if (round.current && status === ACTOR_STATUS.pending) return { key: 'pending' }
  if (history?.action != null) return { key: 'handled', action: history.action }
  if (!round.current && status <= ACTOR_STATUS.pending) return { key: 'skipped' }
  if (status === ACTOR_STATUS.skipped) return { key: 'skipped' }
  return { key: 'handled', action: history?.action }
}

export function participantName(participant: WfTaskParticipant): string {
  return participant.displayName?.trim() || `#${participant.userId}`
}

export function progressParticipants(progress: WfApprovalNodeProgress): WfTaskParticipant[] {
  return progress.currentRounds
    .flatMap((round) => round.task?.participants ?? [])
    .sort((a, b) => Number(a.sort ?? 0) - Number(b.sort ?? 0))
}

const COUNTERSIGN_MODES = new Set(['all', 'seq', '2', '3'])

/** 会签/顺序会签的已同意人数。少于两人、或签、以及已跳过的人不计入。 */
export function countersignProgress(round: WfApprovalRound): { done: number; total: number } | null {
  if (!COUNTERSIGN_MODES.has(String(round.task?.approvalMode ?? ''))) return null
  const active = (round.task?.participants ?? []).filter((item) => Number(item.status) !== ACTOR_STATUS.skipped)
  if (active.length < 2) return null
  return {
    done: active.filter((item) => Number(item.status) === ACTOR_STATUS.done).length,
    total: active.length,
  }
}

function appendUnattributedHistory(
  rounds: readonly WfApprovalRound[],
  groups: Map<'pending' | 'waiting' | 'handled' | 'skipped', Array<{ name: string; action?: string; automatic?: boolean }>>,
) {
  for (const round of rounds) {
    const participants = round.task?.participants ?? []
    const claimed = new Set(
      participants
        .map((participant) => participantHistory(participant, round))
        .filter((item): item is WfHisTask => item != null),
    )
    for (const item of round.history) {
      if (claimed.has(item)) continue
      const name = item.operatorName?.trim() || (Number(item.userId) > 0 ? `#${item.userId}` : '')
      groups.set('handled', [...(groups.get('handled') ?? []), {
        name,
        action: normalizeTaskAction(item.action),
        ...(item.isAutomatic ? { automatic: true } : {}),
      }])
    }
  }
}

export function approvalCardGroups(progress: WfApprovalNodeProgress): Array<{
  key: 'pending' | 'waiting' | 'handled' | 'skipped'
  items: Array<{ name: string; action?: string; automatic?: boolean }>
}> {
  const current = progressParticipants(progress)
  if (current.length) {
    const groups = new Map<'pending' | 'waiting' | 'handled' | 'skipped', Array<{ name: string; action?: string; automatic?: boolean }>>()
    progress.currentRounds.forEach((round) => {
      ;(round.task?.participants ?? []).forEach((participant) => {
        const state = participantState(participant, round)
        const history = participantHistory(participant, round)
        const item = {
          name: participantName(participant),
          action: state.action == null ? undefined : normalizeTaskAction(state.action),
          ...(history?.isAutomatic ? { automatic: true } : {}),
        }
        groups.set(state.key, [...(groups.get(state.key) ?? []), item])
      })
    })
    appendUnattributedHistory(progress.currentRounds, groups)
    return (['handled', 'pending', 'waiting', 'skipped'] as const)
      .map((key) => ({ key, items: groups.get(key) ?? [] }))
      .filter((group) => group.items.length > 0)
  }

  const latest = progress.pastRounds.at(-1)
  if (!latest) return []
  if (latest.task?.participants?.length) {
    const groups = new Map<'pending' | 'waiting' | 'handled' | 'skipped', Array<{ name: string; action?: string; automatic?: boolean }>>()
    latest.task.participants.forEach((participant) => {
      const state = participantState(participant, latest)
      const key = state.key === 'handled' ? 'handled' : 'skipped'
      const action = state.action
      groups.set(key, [...(groups.get(key) ?? []), {
        name: participantName(participant),
        action: action == null ? undefined : normalizeTaskAction(action),
        ...(participantHistory(participant, latest)?.isAutomatic ? { automatic: true } : {}),
      }])
    })
    appendUnattributedHistory([latest], groups)
    return (['handled', 'skipped'] as const)
      .map((key) => ({ key, items: groups.get(key) ?? [] }))
      .filter((group) => group.items.length > 0)
  }
  return [{
    key: 'handled',
    items: latest.history.map((item) => ({
      name: item.operatorName?.trim() || (Number(item.userId) > 0 ? `#${item.userId}` : ''),
      action: normalizeTaskAction(item.action),
      ...(item.isAutomatic ? { automatic: true } : {}),
    })),
  }]
}
