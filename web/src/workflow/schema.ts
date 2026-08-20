/**
 * 流程 JSON schema v1 —— 与后端 `TenonAdmin.Workflow.Schema` / 设计草案 §二 对齐。
 * 框架无关:不 import Vue/React,供 Vue 设计器与日后 React port 共用(复制即可)。
 */

export const WF_MODEL_VERSION = 1 as const

/** 流程节点类型;M2a 启用 branch,parallel/webhook 留到 M3。 */
export type WfNodeType = 'start' | 'approval' | 'cc' | 'branch' | 'parallel' | 'webhook'

export type WfApprovalMode = 'any' | 'all' | 'seq'
export type WfRejectAction = 'terminate' | 'toNode'
export type WfNobodyAction = 'autoPass' | 'transfer' | 'block'
export type WfReturnPolicy = 'prev' | 'any' | 'node'
export type WfConditionLogic = 'and' | 'or'
export type WfConditionOp =
  | 'eq'
  | 'ne'
  | 'gt'
  | 'gte'
  | 'lt'
  | 'lte'
  | 'in'
  | 'notIn'
  | 'contains'
  | 'empty'
  | 'notEmpty'

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

/** 结构化条件:叶子使用 field/op/value,组使用 logic/children。 */
export interface WfConditionExpr {
  field?: string | null
  op?: WfConditionOp | null
  value?: unknown
  logic?: WfConditionLogic | null
  children?: WfConditionExpr[] | null
}

/** 条件分支的一条臂;默认臂的 expr 可空。 */
export interface WfBranchArm {
  id: string
  name: string
  expr?: WfConditionExpr | null
  isDefault: boolean
  next?: WfNode | null
}

export interface WfNode {
  id: string
  type: WfNodeType
  name: string
  props?: WfNodeProps
  /** 仅 branch(M2a);须恰好一条默认臂。 */
  conditions?: WfBranchArm[]
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

/** M2a 可发布的节点类型。 */
export const WF_M2A_NODE_TYPES: ReadonlySet<WfNodeType> = new Set(['start', 'approval', 'cc', 'branch'])
