import { reactive } from 'vue'
import { describe, expect, it } from 'vitest'
import {
  cloneModel,
  cloneNode,
  addBranchArm,
  addParallelArm,
  createApprovalNode,
  createBranchNode,
  createCcNode,
  createDefaultModel,
  createNode,
  createParallelNode,
  createWebhookNode,
  findNode,
  flattenChain,
  insertAfter,
  insertIntoBranchArm,
  insertIntoParallelArm,
  removeNode,
  removeBranchArm,
  removeParallelArm,
  validateModel,
} from './model'
import type { WfNode, WfParallelArm } from './schema'

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

  it('rejects conditions on non-branch nodes and accepts parallel as a supported type', () => {
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
    expect(validateModel(model)).not.toContainEqual({ code: 'unsupportedType', nodeId: 'parallel', type: 'parallel' })
  })
})

describe('workflow/model M3a-2 parallel schema', () => {
  it('creates parallel nodes with two editable arms and mutates only their local chains', () => {
    const parallel = createParallelNode({ id: 'parallel' })
    expect(parallel.parallelArms).toHaveLength(2)
    expect(parallel.parallelArms?.every((arm) => arm.next === null)).toBe(true)
    expect(createNode('parallel').type).toBe('parallel')

    const added = addParallelArm(parallel, { id: 'extra', name: 'Extra' })
    expect(added?.next).toBeNull()
    expect(parallel.parallelArms).toHaveLength(3)
    expect(removeParallelArm(parallel, 'extra')).toBe(true)
    expect(removeParallelArm(parallel, 'missing')).toBe(false)

    const model = createDefaultModel()
    const join = createCcNode({ id: 'join' })
    parallel.next = join
    model.root.next = parallel
    const approval = createApprovalNode({ id: 'arm-approval' })
    expect(insertIntoParallelArm(model.root, 'parallel', parallel.parallelArms![0]!.id, approval)).toBe(true)
    expect(parallel.parallelArms![0]!.next).toBe(approval)
    expect(parallel.next).toBe(join)
    expect(insertIntoParallelArm(model.root, 'missing', 'arm', createCcNode())).toBe(false)
  })

  it('flattens arms before the join successor and accepts empty arms', () => {
    const emptyArm: WfParallelArm = { id: 'empty', name: '空臂', next: null }
    const join = createCcNode({ id: 'join' })
    const parallel: WfNode = {
      id: 'parallel',
      type: 'parallel',
      name: '并行审批',
      parallelArms: [
        { id: 'finance', name: '财务', next: createApprovalNode({ id: 'finance-approval' }) },
        emptyArm,
      ],
      next: join,
    }
    const model = createDefaultModel()
    model.root.next = parallel

    expect(flattenChain(model.root).map((node) => node.id)).toEqual([
      'start', 'parallel', 'finance-approval', 'join',
    ])
    expect(validateModel(model)).toEqual([])
  })

  it('reports distinguishable arm count, empty id, and duplicate id issues', () => {
    const model = createDefaultModel()
    const parallel: WfNode = {
      id: 'parallel',
      type: 'parallel',
      name: '',
      parallelArms: [{ id: 'only', name: '', next: null }],
      next: null,
    }
    model.root.next = parallel

    expect(validateModel(model)).toContainEqual({ code: 'parallelArmCount', nodeId: 'parallel' })

    parallel.parallelArms = [
      { id: '  ', name: '', next: null },
      { id: 'valid', name: '', next: null },
    ]
    expect(validateModel(model)).toContainEqual({ code: 'emptyParallelArmId', nodeId: 'parallel' })

    parallel.parallelArms[0]!.id = 'valid'
    expect(validateModel(model)).toContainEqual({
      code: 'duplicateParallelArmId',
      nodeId: 'parallel',
      armId: 'valid',
    })
  })

  it('distinguishes nested parallel nodes from reject targets outside the current arm', () => {
    const nested: WfNode = {
      id: 'nested',
      type: 'parallel',
      name: '',
      parallelArms: [
        { id: 'nested-a', name: '', next: null },
        { id: 'nested-b', name: '', next: null },
      ],
      next: null,
    }
    const source = createApprovalNode({
      id: 'source',
      props: { onReject: 'toNode', rejectToNodeId: 'other-arm' },
      next: nested,
    })
    const parallel: WfNode = {
      id: 'parallel',
      type: 'parallel',
      name: '',
      parallelArms: [
        { id: 'a', name: '', next: source },
        { id: 'b', name: '', next: createApprovalNode({ id: 'other-arm' }) },
      ],
      next: null,
    }
    const model = createDefaultModel()
    model.root.next = parallel

    expect(validateModel(model)).toContainEqual({ code: 'nestedParallel', nodeId: 'nested' })
    expect(validateModel(model)).toContainEqual({
      code: 'parallelRejectTargetOutsideArm',
      nodeId: 'source',
      armId: 'a',
      targetNodeId: 'other-arm',
    })
  })
})

describe('workflow/model approval ratio', () => {
  it('defaults missing ratios to 100 and rejects invalid all or sequential ratios', () => {
    const model = createDefaultModel()
    const approval = createApprovalNode({ id: 'approval', props: { mode: 'all' } })
    model.root.next = approval

    expect(validateModel(model)).toEqual([])
    approval.props!.allPassRatio = 0
    expect(validateModel(model)).toContainEqual({ code: 'allPassRatioInvalid', nodeId: 'approval' })
    approval.props!.mode = 'seq'
    approval.props!.allPassRatio = 75
    expect(validateModel(model)).toContainEqual({ code: 'allPassRatioInvalid', nodeId: 'approval' })
    approval.props!.mode = 'any'
    expect(validateModel(model)).toEqual([])
  })
})

describe('workflow/model M3a-2 webhook', () => {
  it('creates an insertable webhook with backend defaults and all six props', () => {
    const webhook = createWebhookNode({ id: 'webhook' })

    expect(webhook).toEqual({
      id: 'webhook',
      type: 'webhook',
      name: '',
      props: {
        webhookUrl: '',
        webhookMethod: 'POST',
        webhookHeaders: {},
        webhookTimeoutSeconds: 30,
        webhookOnFailure: 'fail',
        maxAttempts: 3,
      },
      next: null,
    })
    expect(createNode('webhook').type).toBe('webhook')
  })

  it('preserves webhook props through clone and JSON round trips', () => {
    const model = createDefaultModel()
    model.root.next = createWebhookNode({
      id: 'webhook',
      props: {
        webhookUrl: 'https://example.com/hooks/order',
        webhookMethod: 'PATCH',
        webhookHeaders: { Authorization: 'Bearer token', 'X-Trace': null },
        webhookTimeoutSeconds: 45,
        webhookOnFailure: 'manual',
        maxAttempts: 8,
      },
    })

    expect(cloneModel(model)).toEqual(model)
    expect(JSON.parse(JSON.stringify(model))).toEqual(model)
    expect(validateModel(model)).toEqual([])
  })

  it('reports distinguishable issues for invalid webhook settings', () => {
    const model = createDefaultModel()
    model.root.next = createWebhookNode({
      id: 'webhook',
      props: {
        webhookUrl: 'ftp://example.com/hook',
        webhookMethod: 'TRACE',
        webhookHeaders: {},
        webhookTimeoutSeconds: 0,
        webhookOnFailure: 'fail',
        maxAttempts: 101,
      },
    })

    expect(validateModel(model)).toEqual([
      { code: 'webhookUrlInvalid', nodeId: 'webhook' },
      { code: 'webhookMethodInvalid', nodeId: 'webhook' },
      { code: 'webhookTimeoutOutOfRange', nodeId: 'webhook' },
      { code: 'maxAttemptsOutOfRange', nodeId: 'webhook' },
    ])
  })

  it('keeps legacy start, approval, cc, and branch models valid', () => {
    const model = createDefaultModel()
    const approval = createApprovalNode({ id: 'approval' })
    const cc = createCcNode({ id: 'cc' })
    const branch = createBranchNode({ id: 'branch' })
    model.root.next = approval
    approval.next = cc
    cc.next = branch

    expect(validateModel(model)).toEqual([])
  })
})
