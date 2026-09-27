/** 实例状态 / 任务动作数字表 — 与 Vue detail 对齐,不另起体系。 */
import type { WfInstanceStatus, WfTaskAction } from '@/types/workflow'

const INSTANCE_STATUS: Record<number, string> = {
  1: 'running',
  2: 'approved',
  3: 'rejected',
  4: 'cancelled',
  5: 'terminated',
}

// 值取自后端 WfTaskAction:7 是 Urge(不落 wf_his_task,故与 Vue 一样不给文案),8/9/10 才是加签/减签/拿回。
const TASK_ACTION: Record<number, string> = {
  1: 'approve',
  2: 'reject',
  3: 'transfer',
  4: 'return',
  5: 'withdraw',
  6: 'delegate',
  8: 'addSign',
  9: 'removeSign',
  10: 'takeBack',
}

export function normalizeInstanceStatus(s: WfInstanceStatus | undefined): string {
  if (s == null) return 'unknown'
  return INSTANCE_STATUS[s] ?? 'unknown'
}

export function instanceStatusColor(
  s: WfInstanceStatus | undefined,
): 'default' | 'processing' | 'success' | 'error' | 'warning' {
  const key = normalizeInstanceStatus(s)
  if (key === 'approved') return 'success'
  if (key === 'rejected' || key === 'terminated') return 'error'
  if (key === 'cancelled') return 'warning'
  if (key === 'running') return 'processing'
  return 'default'
}

export function normalizeTaskAction(a: WfTaskAction | undefined): string {
  if (a == null) return 'unknown'
  return TASK_ACTION[a] ?? 'unknown'
}

export function taskActionColor(
  a: WfTaskAction | undefined,
): 'default' | 'processing' | 'success' | 'error' | 'warning' {
  if (a === 1) return 'success'
  if (a === 2) return 'error'
  if (a === 3) return 'warning'
  return 'processing'
}

export function formatDateTime(raw: string | null | undefined): string {
  if (!raw) return '—'
  return raw.replace('T', ' ').slice(0, 16)
}

export const DEF_STATUS = { DRAFT: 0, PUBLISHED: 1, DISABLED: 2 } as const
