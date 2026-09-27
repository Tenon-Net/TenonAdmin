/**
 * 钉钉树模型操作(框架无关)。M2a 支持串行主链与 branch 条件臂子链。
 */
import {
  WF_M3A2_NODE_TYPES,
  WF_MODEL_VERSION,
  type WfBranchArm,
  type WfModel,
  type WfNode,
  type WfParallelArm,
  type WfInsertableNodeType,
} from './schema'

let _seq = 0

interface WfWalkFrame {
  node: WfNode
  previous: WfNode | null
  parallelArm: WfParallelArm | null
  replace: (replacement: WfNode | null) => void
}

/** 确定性 DFS:当前主链节点 → 各臂子链 → 当前节点的 join/主链后继。 */
function walkTree(root: WfNode, visit: (frame: WfWalkFrame) => boolean): boolean {
  function walkChain(
    first: WfNode | null | undefined,
    setFirst: (node: WfNode | null) => void,
    parallelArm: WfParallelArm | null = null,
  ): boolean {
    let previous: WfNode | null = null
    let current = first ?? null
    while (current) {
      const next = current.next ?? null
      if (visit({
        node: current,
        previous,
        parallelArm,
        replace: (replacement) => {
          if (previous) previous.next = replacement
          else setFirst(replacement)
        },
      })) return true
      if (current.type === 'branch') {
        for (const arm of current.conditions ?? []) {
          if (walkChain(arm.next, (replacement) => { arm.next = replacement }, parallelArm)) return true
        }
      }
      if (current.type === 'parallel') {
        for (const arm of current.parallelArms ?? []) {
          if (walkChain(arm.next, (replacement) => { arm.next = replacement }, arm)) return true
        }
      }
      previous = current
      current = next
    }
    return false
  }

  return walkChain(root, () => undefined)
}

/** 生成设计器侧节点 Id(稳定、可读;非雪花)。 */
export function newNodeId(prefix = 'n'): string {
  _seq += 1
  return `${prefix}${Date.now().toString(36)}${_seq.toString(36)}`
}

/** 出厂默认模型:仅发起人根节点(与后端 CreateDefaultModel 对齐)。 */
export function createDefaultModel(): WfModel {
  return {
    version: WF_MODEL_VERSION,
    root: { id: 'start', type: 'start', name: '', next: null },
  }
}

export function createApprovalNode(partial?: Partial<WfNode>): WfNode {
  return {
    id: partial?.id ?? newNodeId('ap'),
    type: 'approval',
    name: partial?.name ?? '',
    props: {
      assignee: { provider: 'leader', params: { level: 1 } },
      mode: 'any',
      nobody: 'autoPass',
      returnPolicy: 'prev',
      formPerms: [],
      ...partial?.props,
    },
    next: partial?.next ?? null,
  }
}

export function createCcNode(partial?: Partial<WfNode>): WfNode {
  return {
    id: partial?.id ?? newNodeId('cc'),
    type: 'cc',
    name: partial?.name ?? '',
    props: {
      assignee: { provider: 'user', params: { userIds: [] as number[] } },
      ...partial?.props,
    },
    next: partial?.next ?? null,
  }
}

export function createBranchArm(partial?: Partial<WfBranchArm>): WfBranchArm {
  return {
    id: partial?.id ?? newNodeId('arm'),
    name: partial?.name ?? '',
    expr: partial && 'expr' in partial ? partial.expr : { logic: 'and', children: [] },
    isDefault: partial?.isDefault ?? false,
    next: partial?.next ?? null,
  }
}

export function createBranchNode(partial?: Partial<WfNode>): WfNode {
  return {
    id: partial?.id ?? newNodeId('br'),
    type: 'branch',
    name: partial?.name ?? '',
    props: partial?.props,
    conditions: partial?.conditions ?? [
      createBranchArm(),
      createBranchArm({ isDefault: true, expr: null }),
    ],
    next: partial?.next ?? null,
  }
}

export function createParallelArm(partial?: Partial<WfParallelArm>): WfParallelArm {
  return {
    id: partial?.id ?? newNodeId('parm'),
    name: partial?.name ?? '',
    next: partial?.next ?? null,
  }
}

export function createParallelNode(partial?: Partial<WfNode>): WfNode {
  return {
    id: partial?.id ?? newNodeId('par'),
    type: 'parallel',
    name: partial?.name ?? '',
    props: partial?.props,
    parallelArms: partial?.parallelArms ?? [createParallelArm(), createParallelArm()],
    next: partial?.next ?? null,
  }
}

export function createWebhookNode(partial?: Partial<WfNode>): WfNode {
  return {
    id: partial?.id ?? newNodeId('wh'),
    type: 'webhook',
    name: partial?.name ?? '',
    props: {
      webhookUrl: '',
      webhookMethod: 'POST',
      webhookHeaders: {},
      webhookTimeoutSeconds: 30,
      webhookOnFailure: 'fail',
      maxAttempts: 3,
      ...partial?.props,
    },
    next: partial?.next ?? null,
  }
}

export function createNode(type: WfInsertableNodeType): WfNode {
  if (type === 'approval') return createApprovalNode()
  if (type === 'cc') return createCcNode()
  if (type === 'parallel') return createParallelNode()
  if (type === 'webhook') return createWebhookNode()
  return createBranchNode()
}

/** 在唯一默认臂之前新增普通臂;非法 branch 形状不擅自修复。 */
export function addBranchArm(branch: WfNode, partial?: Partial<WfBranchArm>): WfBranchArm | null {
  if (branch.type !== 'branch') return null
  const arms = branch.conditions ?? []
  const defaultIndexes = arms
    .map((arm, index) => arm.isDefault ? index : -1)
    .filter((index) => index >= 0)
  if (defaultIndexes.length !== 1) return null
  const arm = createBranchArm({ ...partial, expr: partial?.expr ?? { logic: 'and', children: [] }, isDefault: false })
  arms.splice(defaultIndexes[0]!, 0, arm)
  branch.conditions = arms
  return arm
}

/** 删除普通臂;默认臂是 branch 的兜底语义,不可删除。 */
export function removeBranchArm(branch: WfNode, armId: string): boolean {
  if (branch.type !== 'branch' || !branch.conditions) return false
  const index = branch.conditions.findIndex((arm) => arm.id === armId)
  if (index < 0 || branch.conditions[index]!.isDefault) return false
  branch.conditions.splice(index, 1)
  return true
}

export function addParallelArm(parallel: WfNode, partial?: Partial<WfParallelArm>): WfParallelArm | null {
  if (parallel.type !== 'parallel') return null
  const arm = createParallelArm(partial)
  ;(parallel.parallelArms ??= []).push(arm)
  return arm
}

export function removeParallelArm(parallel: WfNode, armId: string): boolean {
  if (parallel.type !== 'parallel' || !parallel.parallelArms) return false
  const index = parallel.parallelArms.findIndex((arm) => arm.id === armId)
  if (index < 0) return false
  parallel.parallelArms.splice(index, 1)
  return true
}

/** 深度克隆(设计器本地编辑用;JSON 往返可处理 Vue reactive proxy)。 */
export function cloneModel(model: WfModel): WfModel {
  return JSON.parse(JSON.stringify(model)) as WfModel
}

/** 深度克隆单个节点(树增删场景;理由同 {@link cloneModel})。 */
export function cloneNode(node: WfNode): WfNode {
  return JSON.parse(JSON.stringify(node)) as WfNode
}

/** 按确定性 DFS 展开整棵树(不含 null)。 */
export function flattenChain(root: WfNode): WfNode[] {
  const list: WfNode[] = []
  walkTree(root, ({ node }) => {
    list.push(node)
    return false
  })
  return list
}

/** 在 afterId 节点后插入;找不到则 false。 */
export function insertAfter(root: WfNode, afterId: string, node: WfNode): boolean {
  return walkTree(root, ({ node: current }) => {
    if (current.id !== afterId) return false
    node.next = current.next ?? null
    current.next = node
    return true
  })
}

/** 插到指定分支臂的局部链头部;branch.next 是汇合后继,不参与臂内接线。 */
export function insertIntoBranchArm(root: WfNode, branchId: string, armId: string, node: WfNode): boolean {
  const branch = findNode(root, branchId)
  if (branch?.type !== 'branch') return false
  const arm = branch.conditions?.find((item) => item.id === armId)
  if (!arm) return false
  node.next = arm.next ?? null
  arm.next = node
  return true
}

export function insertIntoParallelArm(root: WfNode, parallelId: string, armId: string, node: WfNode): boolean {
  const parallel = findNode(root, parallelId)
  if (parallel?.type !== 'parallel') return false
  const arm = parallel.parallelArms?.find((item) => item.id === armId)
  if (!arm) return false
  node.next = arm.next ?? null
  arm.next = node
  return true
}

/** 删除非 start 节点;找不到或试图删 start 则 false。 */
export function removeNode(root: WfNode, nodeId: string): boolean {
  if (root.id === nodeId) return false
  return walkTree(root, ({ node, replace }) => {
    if (node.id !== nodeId) return false
    replace(node.next ?? null)
    return true
  })
}

/** 按 Id 查找。 */
export function findNode(root: WfNode, nodeId: string): WfNode | null {
  let found: WfNode | null = null
  walkTree(root, ({ node }) => {
    if (node.id !== nodeId) return false
    found = node
    return true
  })
  return found
}

export interface WfModelIssue {
  code:
    | 'rootNotStart'
    | 'emptyNodeId'
    | 'duplicateNodeId'
    | 'unsupportedType'
    | 'conditionsOnNonBranch'
    | 'branchNoArms'
    | 'emptyArmId'
    | 'duplicateArmId'
    | 'branchArmWithoutExpr'
    | 'branchDefaultArmCount'
    | 'parallelArmCount'
    | 'emptyParallelArmId'
    | 'duplicateParallelArmId'
    | 'nestedParallel'
    | 'parallelRejectTargetOutsideArm'
    | 'allPassRatioInvalid'
    | 'webhookUrlInvalid'
    | 'webhookMethodInvalid'
    | 'webhookTimeoutOutOfRange'
    | 'maxAttemptsOutOfRange'
  nodeId?: string
  armId?: string
  targetNodeId?: string
  type?: string
}

/** 与后端 ValidateModelForPublish 对齐的前端校验(保存前可选用)。 */
export function validateModel(model: WfModel): WfModelIssue[] {
  const issues: WfModelIssue[] = []
  if (model.root?.type !== 'start') {
    issues.push({ code: 'rootNotStart' })
    return issues
  }
  const frames: Array<Pick<WfWalkFrame, 'node' | 'parallelArm'>> = []
  walkTree(model.root, ({ node, parallelArm }) => {
    frames.push({ node, parallelArm })
    return false
  })
  const nodeParallelArms = new Map<string, WfParallelArm | null>()
  for (const { node, parallelArm } of frames) {
    if (!nodeParallelArms.has(node.id)) nodeParallelArms.set(node.id, parallelArm)
  }

  const seen = new Set<string>()
  for (const { node, parallelArm } of frames) {
    if (!node.id?.trim()) {
      issues.push({ code: 'emptyNodeId', nodeId: node.id })
      continue
    }
    if (seen.has(node.id)) {
      issues.push({ code: 'duplicateNodeId', nodeId: node.id })
    }
    seen.add(node.id)
    if (!WF_M3A2_NODE_TYPES.has(node.type)) {
      issues.push({ code: 'unsupportedType', nodeId: node.id, type: node.type })
    }
    if (node.type !== 'branch' && node.conditions && node.conditions.length > 0) {
      issues.push({ code: 'conditionsOnNonBranch', nodeId: node.id })
    }
    if (node.type === 'branch') {
      const arms = node.conditions
      if (!arms?.length) {
        issues.push({ code: 'branchNoArms', nodeId: node.id })
      } else {
        const armIds = new Set<string>()
        for (const arm of arms) {
          if (!arm.id?.trim()) {
            issues.push({ code: 'emptyArmId', nodeId: node.id })
          } else if (armIds.has(arm.id)) {
            issues.push({ code: 'duplicateArmId', nodeId: node.id, armId: arm.id })
          }
          armIds.add(arm.id)
          if (!arm.isDefault && arm.expr == null) {
            issues.push({ code: 'branchArmWithoutExpr', nodeId: node.id, armId: arm.id })
          }
        }
        if (arms.filter((arm) => arm.isDefault).length !== 1) {
          issues.push({ code: 'branchDefaultArmCount', nodeId: node.id })
        }
      }
    }
    if (node.type === 'parallel') {
      if (parallelArm) issues.push({ code: 'nestedParallel', nodeId: node.id })
      const arms = node.parallelArms ?? []
      if (arms.length < 2) {
        issues.push({ code: 'parallelArmCount', nodeId: node.id })
      }
      const armIds = new Set<string>()
      for (const arm of arms) {
        if (!arm.id?.trim()) {
          issues.push({ code: 'emptyParallelArmId', nodeId: node.id })
        } else if (armIds.has(arm.id)) {
          issues.push({ code: 'duplicateParallelArmId', nodeId: node.id, armId: arm.id })
        }
        armIds.add(arm.id)
      }
    }
    const rejectTargetId = node.props?.onReject === 'toNode' ? node.props.rejectToNodeId : undefined
    if (rejectTargetId && nodeParallelArms.has(rejectTargetId)) {
      const targetArm = nodeParallelArms.get(rejectTargetId)!
      if (parallelArm !== targetArm && (parallelArm !== null || targetArm !== null)) {
        issues.push({
          code: 'parallelRejectTargetOutsideArm',
          nodeId: node.id,
          armId: parallelArm?.id,
          targetNodeId: rejectTargetId,
        })
      }
    }
    if (node.type === 'approval') {
      const mode = node.props?.mode ?? 'any'
      const ratio = node.props?.allPassRatio
      if (
        (mode === 'all' && ratio !== undefined && (!Number.isInteger(ratio) || ratio < 1 || ratio > 100))
        || (mode === 'seq' && ratio !== undefined && ratio !== 100)
      ) issues.push({ code: 'allPassRatioInvalid', nodeId: node.id })
    }
    if (node.type === 'webhook') {
      const props = node.props
      let url: URL | null = null
      try {
        url = new URL(props?.webhookUrl ?? '')
      } catch {
        // 交给下方统一产出可区分 issue。
      }
      if (!url || (url.protocol !== 'http:' && url.protocol !== 'https:')) {
        issues.push({ code: 'webhookUrlInvalid', nodeId: node.id })
      }
      const method = (props?.webhookMethod ?? 'POST').trim().toUpperCase()
      if (!['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD'].includes(method)) {
        issues.push({ code: 'webhookMethodInvalid', nodeId: node.id })
      }
      const timeout = props?.webhookTimeoutSeconds
      if (timeout !== undefined && (!Number.isInteger(timeout) || timeout < 1 || timeout > 120)) {
        issues.push({ code: 'webhookTimeoutOutOfRange', nodeId: node.id })
      }
      const maxAttempts = props?.maxAttempts
      if (maxAttempts !== undefined && (!Number.isInteger(maxAttempts) || maxAttempts < 1 || maxAttempts > 100)) {
        issues.push({ code: 'maxAttemptsOutOfRange', nodeId: node.id })
      }
    }
  }
  return issues
}
