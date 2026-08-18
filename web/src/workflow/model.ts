/**
 * 钉钉树模型操作(框架无关)。M1 仅串行链:start → approval|cc → … → null。
 */
import {
  WF_M1_NODE_TYPES,
  WF_MODEL_VERSION,
  type WfModel,
  type WfNode,
  type WfNodeType,
} from './schema'

let _seq = 0

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

export function createNode(type: Extract<WfNodeType, 'approval' | 'cc'>): WfNode {
  return type === 'approval' ? createApprovalNode() : createCcNode()
}

/** 深度克隆(设计器本地编辑用;不依赖 structuredClone 的特殊类型)。 */
export function cloneModel(model: WfModel): WfModel {
  return JSON.parse(JSON.stringify(model)) as WfModel
}

/** 深度克隆单个节点(树增删场景;理由同 {@link cloneModel})。 */
export function cloneNode(node: WfNode): WfNode {
  return JSON.parse(JSON.stringify(node)) as WfNode
}

/** 按 next 展开为数组(不含 null)。 */
export function flattenChain(root: WfNode): WfNode[] {
  const list: WfNode[] = []
  let cur: WfNode | null | undefined = root
  while (cur) {
    list.push(cur)
    cur = cur.next ?? null
  }
  return list
}

/** 在 afterId 节点后插入;找不到则 false。 */
export function insertAfter(root: WfNode, afterId: string, node: WfNode): boolean {
  let cur: WfNode | null | undefined = root
  while (cur) {
    if (cur.id === afterId) {
      node.next = cur.next ?? null
      cur.next = node
      return true
    }
    cur = cur.next ?? null
  }
  return false
}

/** 删除非 start 节点;找不到或试图删 start 则 false。 */
export function removeNode(root: WfNode, nodeId: string): boolean {
  if (root.id === nodeId) return false
  let prev: WfNode | null = null
  let cur: WfNode | null | undefined = root
  while (cur) {
    if (cur.id === nodeId) {
      if (prev) prev.next = cur.next ?? null
      return true
    }
    prev = cur
    cur = cur.next ?? null
  }
  return false
}

/** 按 Id 查找。 */
export function findNode(root: WfNode, nodeId: string): WfNode | null {
  let cur: WfNode | null | undefined = root
  while (cur) {
    if (cur.id === nodeId) return cur
    cur = cur.next ?? null
  }
  return null
}

export interface WfModelIssue {
  code: 'rootNotStart' | 'emptyNodeId' | 'duplicateNodeId' | 'unsupportedType' | 'hasBranch'
  nodeId?: string
  type?: string
}

/** 与后端 ValidateModelForPublish 对齐的前端校验(保存前可选用)。 */
export function validateM1Model(model: WfModel): WfModelIssue[] {
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
    if (!WF_M1_NODE_TYPES.has(node.type)) {
      issues.push({ code: 'unsupportedType', nodeId: node.id, type: node.type })
    }
    if (node.conditions && node.conditions.length > 0) {
      issues.push({ code: 'hasBranch', nodeId: node.id })
    }
  }
  return issues
}
