/**
 * 流程 JSON schema v1 —— 与后端 `TenonAdmin.Workflow.Schema` / 设计草案 §二 对齐。
 * 框架无关:不 import Vue/React,供 Vue 设计器与日后 React port 共用(复制即可)。
 */

export const WF_MODEL_VERSION = 1 as const

/** M1 启用的节点类型;branch/parallel/webhook 留到 M2/M3。 */
export type WfNodeType = 'start' | 'approval' | 'cc' | 'branch' | 'parallel' | 'webhook'

export type WfApprovalMode = 'any' | 'all' | 'seq'
export type WfRejectAction = 'terminate' | 'toNode'
export type WfNobodyAction = 'autoPass' | 'transfer' | 'block'
export type WfReturnPolicy = 'prev' | 'any' | 'node'

/** 内置 8 种审批人 Provider 键(与 ApproverProviderKeys 对齐)。 */
export type WfAssigneeProvider =
  | 'user'
  | 'leader'
  | 'multiLeader'
  | 'role'
  | 'position'
  | 'selfSelect'
  | 'initiator'
  | 'orgLeader'

export interface WfAssignee {
  provider: WfAssigneeProvider | string
  /** Provider 自定参数(level / roleId / userIds …)。 */
  params?: Record<string, unknown>
}

export interface WfInitiatorScopeItem {
  type: 'user' | 'role' | 'org' | string
  id: number
}

export interface WfNodeProps {
  initiatorScope?: WfInitiatorScopeItem[]
  assignee?: WfAssignee
  mode?: WfApprovalMode
  allPassRatio?: number
  onReject?: WfRejectAction
  rejectToNodeId?: string
  returnPolicy?: WfReturnPolicy
  returnToNodeId?: string
  nobody?: WfNobodyAction
  nobodyTransferUserId?: number
  /** M1 预留空数组;M3 启用字段权限。 */
  formPerms?: unknown[]
  webhookUrl?: string
}

export interface WfNode {
  id: string
  type: WfNodeType
  name: string
  props?: WfNodeProps
  /** 仅 branch(M2);M1 设计器不读写。 */
  conditions?: unknown[]
  next?: WfNode | null
}

export interface WfModel {
  version: number
  root: WfNode
  formSchema?: unknown | null
  formComponent?: string | null
  nobody?: WfNobodyAction | null
  nobodyTransferUserId?: number | null
}

/** M1 可编辑的节点类型(不含 start 的「新增」)。 */
export const WF_M1_INSERTABLE: ReadonlyArray<Extract<WfNodeType, 'approval' | 'cc'>> = [
  'approval',
  'cc',
]

export const WF_M1_NODE_TYPES: ReadonlySet<WfNodeType> = new Set(['start', 'approval', 'cc'])
