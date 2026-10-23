import type { WfInstanceStatus, WfTaskAction } from '@/types/workflow'
import { runtimeStatusKey } from './runtimeStatus'

export type WorkflowTagType = 'default' | 'info' | 'success' | 'error' | 'warning'

const INSTANCE_STATUS_KEYS: Record<number, string> = {
  1: 'running',
  2: 'approved',
  3: 'rejected',
  4: 'cancelled',
  5: 'terminated',
}

const TASK_ACTION_KEYS: Record<number, string> = {
  1: 'approve',
  2: 'reject',
  3: 'transfer',
  4: 'return',
  5: 'withdraw',
  6: 'delegate',
  8: 'addSign',
  9: 'removeSign',
  10: 'takeBack',
  11: 'nobody',
  12: 'adminTransfer',
  13: 'adminSkip',
  14: 'adminTerminate',
}

export function instanceStatusKey(status: WfInstanceStatus | undefined): string {
  return status == null ? 'unknown' : INSTANCE_STATUS_KEYS[Number(status)] ?? 'unknown'
}

export function displayedInstanceStatusKey(row: {
  status?: WfInstanceStatus
  returnedToStarter?: boolean | null
  blockedNodeIds?: readonly string[] | null
  hasPendingTask?: boolean | null
} | undefined): string {
  return row ? runtimeStatusKey(row) ?? instanceStatusKey(row.status) : 'unknown'
}

export function instanceStatusTagType(status: WfInstanceStatus | undefined): WorkflowTagType {
  const key = instanceStatusKey(status)
  if (key === 'approved') return 'success'
  if (key === 'rejected' || key === 'terminated') return 'error'
  if (key === 'cancelled') return 'warning'
  if (key === 'running') return 'info'
  return 'default'
}

export function displayedInstanceStatusTagType(row: Parameters<typeof displayedInstanceStatusKey>[0]): WorkflowTagType {
  return row && runtimeStatusKey(row) ? 'warning' : instanceStatusTagType(row?.status)
}

export function taskActionKey(action: WfTaskAction | undefined): string {
  return action == null ? 'unknown' : TASK_ACTION_KEYS[Number(action)] ?? 'unknown'
}

export function taskActionTagType(action: WfTaskAction | undefined): WorkflowTagType {
  if (Number(action) === 1) return 'success'
  if (Number(action) === 2) return 'error'
  if (Number(action) === 3) return 'warning'
  return 'info'
}

export function approvalProgressActionTagType(action: WfTaskAction | undefined): WorkflowTagType {
  if ([3, 4, 10].includes(Number(action))) return 'warning'
  return taskActionTagType(action)
}

export function formatWorkflowDateTime(value?: string | null, precision: 'minute' | 'second' = 'second'): string {
  return value ? value.replace('T', ' ').slice(0, precision === 'minute' ? 16 : 19) : '—'
}
