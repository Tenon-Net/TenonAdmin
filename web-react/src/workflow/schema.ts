/**
 * 流程 JSON schema v1 —— 与后端 `TenonAdmin.Workflow.Schema` / 设计草案 §二 对齐。
 * 框架无关:不 import Vue/React,供 Vue 设计器与日后 React port 共用(复制即可)。
 */

export const WF_MODEL_VERSION = 1 as const

/** 流程节点类型;M2a 启用 branch,M3 启用 parallel/webhook。 */
export type WfNodeType = 'start' | 'approval' | 'cc' | 'branch' | 'parallel' | 'webhook' | 'aiDecision'

export type WfApprovalMode = 'any' | 'all' | 'seq'
export type WfRejectAction = 'terminate' | 'toNode'
export type WfNobodyAction = 'autoPass' | 'transfer' | 'block'
export type WfReturnPolicy = 'prev' | 'any' | 'node'
export type WfTimeoutAction = 'remind' | 'autoPass' | 'autoReject' | 'transfer'
export type WfWebhookFailureAction = 'fail' | 'manual'
export type WfConditionLogic = 'and' | 'or'
export const WF_FORM_FIELD_TYPES = [
  'text', 'textarea', 'number', 'money', 'date', 'datetime', 'select', 'multiSelect', 'user', 'attachment',
] as const
export type WfFormFieldType = typeof WF_FORM_FIELD_TYPES[number]

export interface WfFormOption {
  label: string
  value: string
}

export interface WfFormFieldPropsByType {
  text: { maxLength?: number }
  textarea: { maxLength?: number; rows?: number }
  number: { min?: number; max?: number; precision?: number }
  money: { min?: number; max?: number }
  date: { min?: string; max?: string }
  datetime: { min?: string; max?: string }
  select: { options: WfFormOption[] }
  multiSelect: { options: WfFormOption[]; maxSelected?: number }
  user: { multiple?: boolean; maxSelected?: number }
  attachment: { multiple?: boolean; maxCount?: number; accept?: string; maxSizeMb?: number }
}

interface WfFormFieldBase {
  key: string
  label: string
  required: boolean
  placeholder?: string | null
}

export type WfFormField = {
  [T in WfFormFieldType]: WfFormFieldBase & {
    type: T
    props?: WfFormFieldPropsByType[T] | null
  }
}[WfFormFieldType]

export interface WfFormSchema {
  version: 1
  fields: WfFormField[]
}

export type WfFormFieldOfType<T extends WfFormFieldType> = Extract<WfFormField, { type: T }>

export type WfFormPermAccess = 'hidden' | 'readonly' | 'editable'

export interface WfFormFieldPerm {
  field: string
  access?: WfFormPermAccess
}

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

export interface WfTimeout {
  hours: number
  action: WfTimeoutAction
  transferUserId?: number
}

export interface WfButtonLabels {
  approve?: string
  reject?: string
  return?: string
  transfer?: string
  delegate?: string
  urge?: string
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
  timeout?: WfTimeout
  buttonLabels?: WfButtonLabels
  nobody?: WfNobodyAction
  nobodyTransferUserId?: number
  /** 审批节点字段权限;缺失项按 editable 处理。 */
  formPerms?: WfFormFieldPerm[]
  webhookUrl?: string
  webhookMethod?: string
  webhookHeaders?: Record<string, string | null>
  webhookTimeoutSeconds?: number
  webhookOnFailure?: WfWebhookFailureAction
  maxAttempts?: number
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

/** 并行节点的一条臂;next 为空表示空臂。 */
export interface WfParallelArm {
  id: string
  name: string
  next?: WfNode | null
}

export interface WfNode {
  id: string
  type: WfNodeType
  name: string
  props?: WfNodeProps
  /** 仅 branch(M2a);须恰好一条默认臂。 */
  conditions?: WfBranchArm[]
  /** 仅 parallel;节点 next 是各臂完成后的 join 后继。 */
  parallelArms?: WfParallelArm[]
  next?: WfNode | null
}

export interface WfModel {
  version: number
  root: WfNode
  formSchema?: WfFormSchema | null
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

/** M3a-2 可发布的节点类型。 */
export const WF_M3A2_NODE_TYPES: ReadonlySet<WfNodeType> = new Set([...WF_M2A_NODE_TYPES, 'parallel', 'webhook'])

export type WfInsertableNodeType = Extract<WfNodeType, 'approval' | 'cc' | 'branch' | 'parallel' | 'webhook'>
