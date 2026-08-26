/**
 * 钉钉树模型操作(框架无关)。M2a 支持串行主链与 branch 条件臂子链。
 */
import {
  WF_M2A_NODE_TYPES,
  WF_MODEL_VERSION,
  type WfBranchArm,
  type WfModel,
  type WfNode,
  type WfNodeType,
} from './schema'

let _seq = 0

interface WfWalkFrame {
  node: WfNode
  previous: WfNode | null
  replace: (replacement: WfNode | null) => void
}

/** 确定性 DFS:当前主链节点 → conditions 顺序下的各臂子链 → 当前节点的主链后继。 */
function walkTree(root: WfNode, visit: (frame: WfWalkFrame) => boolean): boolean {
  function walkChain(first: WfNode | null | undefined, setFirst: (node: WfNode | null) => void): boolean {
    let previous: WfNode | null = null
    let current = first ?? null
    while (current) {
      const next = current.next ?? null
      if (visit({
        node: current,
        previous,
        replace: (replacement) => {
          if (previous) previous.next = replacement
          else setFirst(replacement)
        },
      })) return true
      if (current.type === 'branch') {
        for (const arm of current.conditions ?? []) {
          if (walkChain(arm.next, (replacement) => { arm.next = replacement })) return true
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

export function createNode(type: Extract<WfNodeType, 'approval' | 'cc' | 'branch'>): WfNode {
  if (type === 'approval') return createApprovalNode()
  if (type === 'cc') return createCcNode()
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
  nodeId?: string
  armId?: string
  type?: string
}

/** 与后端 ValidateModelForPublish 对齐的前端校验(保存前可选用)。 */
export function validateModel(model: WfModel): WfModelIssue[] {
  const issues: WfModelIssue[] = []
  if (model.root?.type !== 'start') {
    issues.push({ code: 'rootNotStart' })
    return issues
  }
  const seen = new Set<string>()
  for (const node of flattenChain(model.root)) {
    if (!node.id?.trim()) {
      issues.push({ code: 'emptyNodeId', nodeId: node.id })
      continue
    }
    if (seen.has(node.id)) {
      issues.push({ code: 'duplicateNodeId', nodeId: node.id })
    }
    seen.add(node.id)
    if (!WF_M2A_NODE_TYPES.has(node.type)) {
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
  }
  return issues
}
