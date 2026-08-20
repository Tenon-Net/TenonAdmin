import { reactive } from 'vue'
import { describe, expect, it } from 'vitest'
import {
  cloneModel,
  cloneNode,
  addBranchArm,
  createApprovalNode,
  createBranchNode,
  createCcNode,
  createDefaultModel,
  findNode,
  flattenChain,
  insertAfter,
  insertIntoBranchArm,
  removeNode,
  removeBranchArm,
  validateModel,
} from './model'

describe('workflow/model M1 chain', () => {
  it('createDefaultModel is start-only', () => {
    const m = createDefaultModel()
    expect(m.version).toBe(1)
    expect(m.root.type).toBe('start')
    expect(m.root.next).toBeNull()
    expect(validateModel(m)).toEqual([])
  })

  it('insert approval then cc keeps serial order', () => {
    const m = createDefaultModel()
    const ap = createApprovalNode({ name: '部门审批' })
    const cc = createCcNode({ name: '抄送人事' })
    expect(insertAfter(m.root, m.root.id, ap)).toBe(true)
    expect(insertAfter(m.root, ap.id, cc)).toBe(true)
    expect(flattenChain(m.root).map((n) => n.type)).toEqual(['start', 'approval', 'cc'])
    expect(validateModel(m)).toEqual([])
  })

  it('removeNode cannot delete start', () => {
    const m = createDefaultModel()
    const ap = createApprovalNode()
    insertAfter(m.root, 'start', ap)
    expect(removeNode(m.root, 'start')).toBe(false)
    expect(removeNode(m.root, ap.id)).toBe(true)
    expect(m.root.next).toBeNull()
  })

  it('cloneModel deep-copies', () => {
    const m = createDefaultModel()
    insertAfter(m.root, 'start', createApprovalNode())
    const c = cloneModel(m)
    c.root.name = 'X'
    expect(m.root.name).toBe('')
  })

  it('cloneNode deep-copies through the chain', () => {
    const a = createApprovalNode({ name: 'A' })
    insertAfter(a, a.id, createCcNode({ name: 'B' }))
    const c = cloneNode(a)
    c.next!.name = 'Z'
    expect(a.next!.name).toBe('B')
  })

  it('cloneModel/cloneNode survive a reactive proxy (the bug this guards against)', () => {
    const raw = createDefaultModel()
    const branch = createBranchNode({ id: 'branch' })
    branch.conditions![0]!.next = createApprovalNode({ id: 'arm-deep', name: '原名称' })
    raw.root.next = branch
    const modelProxy = reactive(raw)
    const nodeProxy = reactive(branch)

    const modelClone = cloneModel(modelProxy)
    const nodeClone = cloneNode(nodeProxy)
    modelClone.root.next!.conditions![0]!.next!.name = '模型克隆'
    nodeClone.conditions![0]!.next!.name = '节点克隆'

    expect(raw.root.next.conditions![0]!.next!.name).toBe('原名称')
    expect(modelClone.root.next!.conditions![0]!.next!.name).toBe('模型克隆')
    expect(nodeClone.conditions![0]!.next!.name).toBe('节点克隆')
  })
})

describe('workflow/model M2a tree', () => {
  it('flattens main nodes and branch arms in deterministic DFS order', () => {
    const merge = createApprovalNode({ id: 'merge' })
    const armA2 = createCcNode({ id: 'arm-a2' })
    const armA1 = createApprovalNode({ id: 'arm-a1', next: armA2 })
    const armB1 = createCcNode({ id: 'arm-b1' })
    const branch = {
      id: 'branch',
      type: 'branch' as const,
      name: '金额分支',
      conditions: [
        { id: 'high', name: '高金额', expr: { logic: 'and' as const, children: [] }, isDefault: false, next: armA1 },
        { id: 'default', name: '其他', expr: null, isDefault: true, next: armB1 },
      ],
      next: merge,
    }
    const model = createDefaultModel()
    model.root.next = branch

    expect(flattenChain(model.root).map((node) => node.id)).toEqual([
      'start',
      'branch',
      'arm-a1',
      'arm-a2',
      'arm-b1',
      'merge',
    ])
    expect(findNode(model.root, 'arm-a2')).toBe(armA2)
  })

  it('inserts and removes only inside the matched branch arm', () => {
    const merge = createApprovalNode({ id: 'merge' })
    const armA2 = createCcNode({ id: 'arm-a2' })
    const armA1 = createApprovalNode({ id: 'arm-a1', next: armA2 })
    const armB1 = createCcNode({ id: 'arm-b1' })
    const branch = {
      id: 'branch',
      type: 'branch' as const,
      name: '金额分支',
      conditions: [
        { id: 'high', name: '高金额', expr: { logic: 'and' as const, children: [] }, isDefault: false, next: armA1 },
        { id: 'default', name: '其他', expr: null, isDefault: true, next: armB1 },
      ],
      next: merge,
    }
    const model = createDefaultModel()
    model.root.next = branch
    const inserted = createApprovalNode({ id: 'inserted' })

    expect(insertAfter(model.root, 'arm-a1', inserted)).toBe(true)
    expect(flattenChain(model.root).map((node) => node.id)).toEqual([
      'start', 'branch', 'arm-a1', 'inserted', 'arm-a2', 'arm-b1', 'merge',
    ])
    expect(branch.next).toBe(merge)
    expect(removeNode(model.root, 'inserted')).toBe(true)
    expect(armA1.next).toBe(armA2)
    expect(removeNode(model.root, 'arm-a1')).toBe(true)
    expect(branch.conditions[0]!.next).toBe(armA2)
    expect(branch.next).toBe(merge)
  })

  it('inserts twice at an arm head without changing the branch merge successor', () => {
    const model = createDefaultModel()
    const merge = createApprovalNode({ id: 'merge' })
    const branch = createBranchNode({ id: 'branch', next: merge })
    const ordinaryArm = branch.conditions![0]!
    model.root.next = branch
    const approval = createApprovalNode({ id: 'arm-approval' })
    const cc = createCcNode({ id: 'arm-cc' })

    expect(insertIntoBranchArm(model.root, 'branch', ordinaryArm.id, approval)).toBe(true)
    expect(ordinaryArm.next).toBe(approval)
    expect(approval.next).toBeNull()
    expect(insertIntoBranchArm(model.root, 'branch', ordinaryArm.id, cc)).toBe(true)
    expect(ordinaryArm.next).toBe(cc)
    expect(cc.next).toBe(approval)
    expect(branch.next).toBe(merge)
    expect(insertIntoBranchArm(model.root, 'missing-branch', ordinaryArm.id, createCcNode())).toBe(false)
    expect(insertIntoBranchArm(model.root, 'branch', 'missing-arm', createCcNode())).toBe(false)
  })

  it('creates and edits branch arms while preserving the single last default arm', () => {
    const branch = createBranchNode({ id: 'branch' })
    expect(branch.conditions).toHaveLength(2)
    expect(branch.conditions?.map((arm) => arm.isDefault)).toEqual([false, true])
    expect(branch.conditions?.[0]?.expr).toEqual({ logic: 'and', children: [] })
    expect(branch.conditions?.every((arm) => arm.next === null)).toBe(true)

    const added = addBranchArm(branch, { id: 'extra', name: '加签条件' })
    expect(added?.expr).toEqual({ logic: 'and', children: [] })
    expect(branch.conditions?.map((arm) => [arm.id, arm.isDefault])).toEqual([
      [branch.conditions![0]!.id, false],
      ['extra', false],
      [branch.conditions![2]!.id, true],
    ])
    expect(removeBranchArm(branch, branch.conditions![2]!.id)).toBe(false)
    expect(removeBranchArm(branch, 'extra')).toBe(true)
    expect(branch.conditions?.map((arm) => arm.isDefault)).toEqual([false, true])
  })

  it('validates a branch tree and detects duplicate node ids across arm and main chains', () => {
    const model = createDefaultModel()
    const branch = createBranchNode({ id: 'branch' })
    branch.conditions![0]!.next = createApprovalNode({ id: 'shared' })
    branch.next = createCcNode({ id: 'merge' })
    model.root.next = branch

    expect(validateModel(model)).toEqual([])
    branch.next.id = 'shared'
    expect(validateModel(model)).toContainEqual({ code: 'duplicateNodeId', nodeId: 'shared' })
  })

  it('requires branch arms and exactly one default arm', () => {
    const model = createDefaultModel()
    const branch = createBranchNode({ id: 'branch' })
    model.root.next = branch

    branch.conditions = []
    expect(validateModel(model)).toContainEqual({ code: 'branchNoArms', nodeId: 'branch' })

    branch.conditions = [
      { id: 'a', name: 'A', expr: { logic: 'and', children: [] }, isDefault: false, next: null },
    ]
    expect(validateModel(model)).toContainEqual({ code: 'branchDefaultArmCount', nodeId: 'branch' })

    branch.conditions.push({ id: 'd1', name: 'D1', expr: null, isDefault: true, next: null })
    branch.conditions.push({ id: 'd2', name: 'D2', expr: null, isDefault: true, next: null })
    expect(validateModel(model)).toContainEqual({ code: 'branchDefaultArmCount', nodeId: 'branch' })
  })

  it('validates branch arm ids and requires expressions on non-default arms', () => {
    const model = createDefaultModel()
    const branch = createBranchNode({ id: 'branch' })
    model.root.next = branch

    branch.conditions![0]!.id = '  '
    expect(validateModel(model)).toContainEqual({ code: 'emptyArmId', nodeId: 'branch' })

    branch.conditions![0]!.id = branch.conditions![1]!.id
    expect(validateModel(model)).toContainEqual({
      code: 'duplicateArmId',
      nodeId: 'branch',
      armId: branch.conditions![1]!.id,
    })

    branch.conditions![0]!.id = 'ordinary'
    branch.conditions![0]!.expr = null
    expect(validateModel(model)).toContainEqual({
      code: 'branchArmWithoutExpr',
      nodeId: 'branch',
      armId: 'ordinary',
    })
  })

  it('rejects conditions on non-branch nodes and keeps M3 node types unsupported', () => {
    const model = createDefaultModel()
    const approval = createApprovalNode({ id: 'approval' })
    approval.conditions = [
      { id: 'unexpected', name: '', expr: null, isDefault: true, next: null },
    ]
    approval.next = { id: 'parallel', type: 'parallel', name: '', next: {
      id: 'webhook', type: 'webhook', name: '', next: null,
    } }
    model.root.next = approval

    expect(validateModel(model)).toContainEqual({ code: 'conditionsOnNonBranch', nodeId: 'approval' })
    expect(validateModel(model)).toContainEqual({ code: 'unsupportedType', nodeId: 'parallel', type: 'parallel' })
    expect(validateModel(model)).toContainEqual({ code: 'unsupportedType', nodeId: 'webhook', type: 'webhook' })
  })
})
