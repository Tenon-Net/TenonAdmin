import { describe, expect, it } from 'vitest'

import type { WfApprovalMode, WfModel } from './schema'

import {
  WF_CONDITION_OPERATOR_META,
  applyNodeConfiguration,
  appendConditionChild,
  classifyConditionOp,
  createConditionGroup,
  createConditionLeaf,
  isWfConditionOp,
  removeConditionChild,
  replaceConditionChild,
  setConditionOp,
  type WfEditorNodeConfig,
} from './configuration'

describe('workflow node configuration', () => {
  it('publishes one typed classifier for every condition operator', () => {
    expect(WF_CONDITION_OPERATOR_META.map(({ op }) => op)).toEqual([
      'eq', 'ne', 'gt', 'gte', 'lt', 'lte', 'in', 'notIn', 'contains', 'empty', 'notEmpty',
    ])
    expect(classifyConditionOp('gte')).toBe('number')
    expect(classifyConditionOp('in')).toBe('list')
    expect(classifyConditionOp('empty')).toBe('none')
    expect(classifyConditionOp('contains')).toBe('text')
    expect(isWfConditionOp('notEmpty')).toBe(true)
    expect(isWfConditionOp('unknown')).toBe(false)
  })

  it('creates condition groups and leaves with editable defaults', () => {
    expect(createConditionGroup()).toEqual({ logic: 'and', children: [] })
    expect(createConditionGroup('or')).toEqual({ logic: 'or', children: [] })
    expect(createConditionLeaf()).toEqual({
      field: '',
      op: 'eq',
      value: '',
      children: null,
    })
  })

  it('appends a child without changing the source group or its siblings', () => {
    const first = { ...createConditionLeaf(), field: 'amount' }
    const sibling = createConditionGroup('or')
    const source = { ...createConditionGroup(), children: [first, sibling] }
    const added = { ...createConditionLeaf(), field: 'days' }

    const result = appendConditionChild(source, added)

    expect(result).not.toBe(source)
    expect(result?.children).toEqual([first, sibling, added])
    expect(result?.children?.[0]).toBe(first)
    expect(result?.children?.[1]).toBe(sibling)
    expect(source.children).toEqual([first, sibling])
    expect(appendConditionChild(first, added)).toBeNull()
  })

  it('replaces exactly one child without changing the source group', () => {
    const first = { ...createConditionLeaf(), field: 'first' }
    const second = { ...createConditionLeaf(), field: 'second' }
    const third = { ...createConditionLeaf(), field: 'third' }
    const source = { ...createConditionGroup(), children: [first, second, third] }
    const replacement = createConditionGroup('or')

    const result = replaceConditionChild(source, 1, replacement)

    expect(result?.children).toEqual([first, replacement, third])
    expect(result?.children?.[0]).toBe(first)
    expect(result?.children?.[2]).toBe(third)
    expect(source.children).toEqual([first, second, third])
    expect(replaceConditionChild(source, 3, replacement)).toBeNull()
    expect(replaceConditionChild(first, 0, replacement)).toBeNull()
  })

  it('removes the requested child without changing the source group or siblings', () => {
    const first = { ...createConditionLeaf(), field: 'first' }
    const second = { ...createConditionLeaf(), field: 'second' }
    const third = { ...createConditionLeaf(), field: 'third' }
    const source = { ...createConditionGroup(), children: [first, second, third] }

    const result = removeConditionChild(source, 1)

    expect(result?.children).toEqual([first, third])
    expect(result?.children?.[0]).toBe(first)
    expect(result?.children?.[1]).toBe(third)
    expect(source.children).toEqual([first, second, third])
    expect(removeConditionChild(source, -1)).toBeNull()
    expect(removeConditionChild(source, 3)).toBeNull()
    expect(removeConditionChild(first, 0)).toBeNull()
  })

  it('normalizes the editable value shape when the condition operator changes', () => {
    const source = { ...createConditionLeaf(), field: 'amount', value: 'stale' }

    expect(setConditionOp(source, 'gte')).toEqual({ ...source, op: 'gte', value: 0 })
    expect(setConditionOp({ ...source, value: ['a', 2] }, 'in')).toEqual({
      ...source,
      op: 'in',
      value: ['a', '2'],
    })
    expect(setConditionOp(source, 'empty')).toEqual({
      field: 'amount',
      op: 'empty',
      children: null,
    })
    expect(setConditionOp({ ...source, value: 42 }, 'contains')).toEqual({
      ...source,
      op: 'contains',
      value: '42',
    })
    expect(source).toEqual({
      field: 'amount',
      op: 'eq',
      value: 'stale',
      children: null,
    })
  })

  it.each(['all', 'seq'] as WfApprovalMode[])('saves approval mode %s without changing the source model', (mode) => {
    const model: WfModel = {
      version: 1,
      root: {
        id: 'start',
        type: 'start',
        name: 'Start',
        next: {
          id: 'approval',
          type: 'approval',
          name: 'Old approval',
          props: {
            assignee: { provider: 'leader', params: { level: 1 } },
            mode: 'any',
          },
          next: null,
        },
      },
    }
    const snapshot = JSON.stringify(model)

    const result = applyNodeConfiguration(model, 'approval', {
      type: 'approval',
      name: 'Updated approval',
      assignee: { provider: 'user', params: { userIds: [7, 8] } },
      mode,
      returnPolicy: 'prev',
      onReject: 'terminate',
    })

    expect(result?.root.next?.name).toBe('Updated approval')
    expect(result?.root.next?.props).toMatchObject({
      assignee: { provider: 'user', params: { userIds: [7, 8] } },
      mode,
      nobody: 'autoPass',
      onReject: 'terminate',
      returnPolicy: 'prev',
      formPerms: [],
    })
    expect(JSON.stringify(model)).toBe(snapshot)
    expect(result).not.toBe(model)
  })

  it('rejects a configuration whose discriminant does not match the target node', () => {
    const model: WfModel = {
      version: 1,
      root: {
        id: 'start', type: 'start', name: 'Start',
        next: { id: 'approval', type: 'approval', name: 'Approval', next: null },
      },
    }
    const mismatched = {
      type: 'cc',
      name: 'Must not apply',
      assignee: { provider: 'user', params: { userIds: [7] } },
    } as WfEditorNodeConfig

    expect(applyNodeConfiguration(model, 'approval', mismatched)).toBeNull()
    expect(model.root.next?.name).toBe('Approval')
  })

  it('normalizes multi-level manager approvals to sequential mode', () => {
    const model: WfModel = {
      version: 1,
      root: {
        id: 'start',
        type: 'start',
        name: 'Start',
        next: {
          id: 'approval',
          type: 'approval',
          name: 'Approval',
          props: { assignee: { provider: 'leader', params: { level: 1 } }, mode: 'any' },
          next: null,
        },
      },
    }

    const result = applyNodeConfiguration(model, 'approval', {
      type: 'approval',
      name: 'Approval',
      assignee: { provider: 'multiLeader', params: { level: 3 } },
      mode: 'all',
      returnPolicy: 'prev',
      onReject: 'terminate',
    })

    expect(result?.root.next?.props?.mode).toBe('seq')
    expect(model.root.next?.props?.mode).toBe('any')
  })

  it('persists return policy, timeout and button labels on approval save', () => {
    const model: WfModel = {
      version: 1,
      root: {
        id: 'start',
        type: 'start',
        name: 'Start',
        next: {
          id: 'approval',
          type: 'approval',
          name: 'Approval',
          props: { assignee: { provider: 'leader', params: { level: 1 } }, mode: 'any' },
          next: null,
        },
      },
    }

    const result = applyNodeConfiguration(model, 'approval', {
      type: 'approval',
      name: 'Approval',
      assignee: { provider: 'user', params: { userIds: [1] } },
      mode: 'any',
      returnPolicy: 'node',
      returnToNodeId: 'start',
      onReject: 'toNode',
      rejectToNodeId: 'start',
      timeout: { hours: 8, action: 'autoPass' },
      buttonLabels: { approve: '准了', reject: '  ', return: '打回' },
    })

    expect(result?.root.next?.props).toMatchObject({
      returnPolicy: 'node',
      returnToNodeId: 'start',
      onReject: 'toNode',
      rejectToNodeId: 'start',
      timeout: { hours: 8, action: 'autoPass' },
      buttonLabels: { approve: '准了', return: '打回' },
    })
    expect(result?.root.next?.props?.buttonLabels?.reject).toBeUndefined()
    expect(applyNodeConfiguration(model, 'approval', {
      type: 'approval',
      name: 'Approval',
      assignee: { provider: 'user', params: { userIds: [1] } },
      mode: 'any',
      returnPolicy: 'prev',
      onReject: 'terminate',
      timeout: { hours: 0, action: 'remind' },
      buttonLabels: { approve: '   ' },
    })?.root.next?.props).toMatchObject({
      returnPolicy: 'prev',
      onReject: 'terminate',
    })
    expect(applyNodeConfiguration(model, 'approval', {
      type: 'approval',
      name: 'Approval',
      assignee: { provider: 'user', params: { userIds: [1] } },
      mode: 'any',
      returnPolicy: 'prev',
      onReject: 'terminate',
      timeout: { hours: 0, action: 'remind' },
    })?.root.next?.props?.timeout).toBeUndefined()
  })

  it('updates non-default branch expressions by arm id without touching the default arm or assignee', () => {
    const defaultExpr = { ...createConditionLeaf(), field: 'default-marker', value: 'keep' }
    const model: WfModel = {
      version: 1,
      root: {
        id: 'start',
        type: 'start',
        name: 'Start',
        next: {
          id: 'branch',
          type: 'branch',
          name: 'Old branch',
          conditions: [
            { id: 'arm-a', name: 'A', isDefault: false, expr: createConditionGroup(), next: null },
            { id: 'arm-b', name: 'B', isDefault: false, expr: createConditionGroup(), next: null },
            { id: 'default', name: 'Default', isDefault: true, expr: defaultExpr, next: null },
          ],
          next: null,
        },
      },
    }
    const snapshot = JSON.stringify(model)
    const armAExpr = {
      ...createConditionGroup(),
      children: [{ ...createConditionLeaf(), field: 'amount', op: 'gte' as const, value: 100 }],
    }
    const armBExpr = {
      ...createConditionGroup('or'),
      children: [{ ...createConditionLeaf(), field: 'region', value: 'west' }],
    }

    const result = applyNodeConfiguration(model, 'branch', {
      type: 'branch',
      name: 'Updated branch',
      armExpressions: {
        'arm-b': armBExpr,
        'arm-a': armAExpr,
      },
    })
    const branch = result?.root.next

    expect(branch?.name).toBe('Updated branch')
    expect(branch?.conditions?.find((arm) => arm.id === 'arm-a')?.expr).toEqual(armAExpr)
    expect(branch?.conditions?.find((arm) => arm.id === 'arm-b')?.expr).toEqual(armBExpr)
    expect(branch?.conditions?.find((arm) => arm.id === 'default')?.expr).toEqual(defaultExpr)
    expect(branch?.props?.assignee).toBeUndefined()
    expect(branch?.props?.mode).toBeUndefined()
    expect(JSON.stringify(model)).toBe(snapshot)
    expect(applyNodeConfiguration(model, 'branch', {
      type: 'branch',
      name: 'Invalid leaf root',
      armExpressions: { 'arm-a': createConditionLeaf() },
    })).toBeNull()
  })

  it('updates start configuration through the same immutable seam', () => {
    const model: WfModel = {
      version: 1,
      formComponent: 'views/old/form',
      root: { id: 'start', type: 'start', name: 'Old start', next: null },
    }
    const snapshot = JSON.stringify(model)

    const result = applyNodeConfiguration(model, 'start', {
      type: 'start',
      name: 'Updated start',
      formComponent: ' views/biz/leave/form ',
      initiatorScope: [
        { type: 'user', id: 7 },
        { type: 'role', id: 8 },
        { type: 'org', id: 9 },
      ],
    })

    expect(result).toMatchObject({
      formComponent: 'views/biz/leave/form',
      root: {
        name: 'Updated start',
        props: {
          initiatorScope: [
            { type: 'user', id: 7 },
            { type: 'role', id: 8 },
            { type: 'org', id: 9 },
          ],
        },
      },
    })
    expect(JSON.stringify(model)).toBe(snapshot)
  })

  it('updates a cc assignee without writing approval mode', () => {
    const model: WfModel = {
      version: 1,
      root: {
        id: 'start',
        type: 'start',
        name: 'Start',
        next: { id: 'cc', type: 'cc', name: 'Old cc', next: null },
      },
    }

    const result = applyNodeConfiguration(model, 'cc', {
      type: 'cc',
      name: 'Updated cc',
      assignee: { provider: 'role', params: { roleId: 12 } },
    })

    expect(result?.root.next).toMatchObject({
      name: 'Updated cc',
      props: { assignee: { provider: 'role', params: { roleId: 12 } } },
    })
    expect(result?.root.next?.props?.mode).toBeUndefined()
    expect(model.root.next?.props).toBeUndefined()
  })

  it('finds a deeply nested branch and rejects unknown node or arm ids without partial writes', () => {
    const nestedExpr = createConditionGroup('or')
    const model: WfModel = {
      version: 1,
      root: {
        id: 'start',
        type: 'start',
        name: 'Start',
        next: {
          id: 'outer',
          type: 'branch',
          name: 'Outer',
          conditions: [
            {
              id: 'outer-arm',
              name: 'Outer arm',
              isDefault: false,
              expr: createConditionGroup(),
              next: {
                id: 'nested',
                type: 'branch',
                name: 'Nested',
                conditions: [
                  { id: 'nested-arm', name: 'Nested arm', isDefault: false, expr: createConditionGroup(), next: null },
                  { id: 'nested-default', name: 'Default', isDefault: true, expr: null, next: null },
                ],
                next: null,
              },
            },
            { id: 'outer-default', name: 'Default', isDefault: true, expr: null, next: null },
          ],
          next: null,
        },
      },
    }
    const snapshot = JSON.stringify(model)

    const result = applyNodeConfiguration(model, 'nested', {
      type: 'branch',
      name: 'Updated nested',
      armExpressions: { 'nested-arm': nestedExpr },
    })
    const nested = result?.root.next?.conditions?.[0]?.next

    expect(nested?.name).toBe('Updated nested')
    expect(nested?.conditions?.[0]?.expr).toEqual(nestedExpr)
    expect(applyNodeConfiguration(model, 'missing', {
      type: 'start',
      name: 'Missing',
      formComponent: null,
      initiatorScope: [],
    })).toBeNull()
    expect(applyNodeConfiguration(model, 'nested', {
      type: 'branch',
      name: 'Should not leak',
      armExpressions: {
        'nested-arm': nestedExpr,
        missing: createConditionGroup(),
      },
    })).toBeNull()
    expect(JSON.stringify(model)).toBe(snapshot)
  })
})
