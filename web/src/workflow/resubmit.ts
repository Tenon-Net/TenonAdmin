import type { WfHistoryItem } from '@/types/workflow'

interface ResubmitEligibility {
  isStarter: boolean
  isRunning: boolean
  currentTasks: readonly unknown[]
  pendingTasks: readonly unknown[]
  history: readonly Pick<WfHistoryItem, 'eventType' | 'sequence' | 'createTime' | 'id'>[]
  forks: readonly { status?: number | string | null }[]
}

/** 只有最近一次重提之后又发生过退回,且实例确实停在可重提态时才允许发起人重提。 */
export function canResubmitInstance({
  isStarter, isRunning, currentTasks, pendingTasks, history, forks,
}: ResubmitEligibility): boolean {
  if (!isStarter || !isRunning || currentTasks.length > 0 || pendingTasks.length > 0) return false
  if (forks.some((fork) => Number(fork.status) === 1)) return false
  const lifecycle = history
    .filter((item) => Number(item.eventType) === 12 || Number(item.eventType) === 14)
    .sort((a, b) =>
      Number(a.sequence ?? 0) - Number(b.sequence ?? 0)
      || String(a.createTime ?? '').localeCompare(String(b.createTime ?? ''))
      || String(a.id ?? '').localeCompare(String(b.id ?? '')))
  return Number(lifecycle.at(-1)?.eventType) === 14
}
