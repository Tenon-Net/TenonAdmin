import { describe, expect, it } from 'vitest'
import {
  approvalProgressActionTagType,
  displayedInstanceStatusKey,
  displayedInstanceStatusTagType,
  formatWorkflowDateTime,
  instanceStatusKey,
  instanceStatusTagType,
  taskActionKey,
  taskActionTagType,
} from './statusLabels'

describe('workflow status labels', () => {
  it('maps every persisted instance status and keeps unknown values explicit', () => {
    expect([1, 2, 3, 4, 5].map((value) => instanceStatusKey(value))).toEqual([
      'running', 'approved', 'rejected', 'cancelled', 'terminated',
    ])
    expect(instanceStatusKey(undefined)).toBe('unknown')
    expect(instanceStatusKey(99)).toBe('unknown')
    expect(instanceStatusTagType(1)).toBe('info')
    expect(instanceStatusTagType(2)).toBe('success')
    expect(instanceStatusTagType(5)).toBe('error')
  })

  it('gives runtime warning states precedence over the persisted status', () => {
    expect(displayedInstanceStatusKey({ status: 1, returnedToStarter: true })).toBe('returned')
    expect(displayedInstanceStatusKey({ status: 1, blockedNodeIds: ['review'], hasPendingTask: true })).toBe('blockedPartial')
    expect(displayedInstanceStatusTagType({ status: 1, blockedNodeIds: ['review'] })).toBe('warning')
    expect(displayedInstanceStatusKey(undefined)).toBe('unknown')
  })

  it('maps persisted task actions and preserves the progress warning semantics', () => {
    expect(taskActionKey(1)).toBe('approve')
    expect(taskActionKey(14)).toBe('adminTerminate')
    expect(taskActionKey(7)).toBe('unknown')
    expect(taskActionTagType(4)).toBe('info')
    expect(approvalProgressActionTagType(4)).toBe('warning')
    expect(approvalProgressActionTagType(10)).toBe('warning')
  })

  it('formats API timestamps without depending on the browser locale', () => {
    expect(formatWorkflowDateTime('2026-10-23T09:08:07.123Z')).toBe('2026-10-23 09:08:07')
    expect(formatWorkflowDateTime('2026-10-23T09:08:07.123Z', 'minute')).toBe('2026-10-23 09:08')
    expect(formatWorkflowDateTime('2026-10-23')).toBe('2026-10-23')
    expect(formatWorkflowDateTime(null)).toBe('—')
    expect(formatWorkflowDateTime('')).toBe('—')
  })
})
