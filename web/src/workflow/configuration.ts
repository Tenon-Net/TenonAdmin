import type {
  WfApprovalMode,
  WfAssignee,
  WfButtonLabels,
  WfConditionExpr,
  WfConditionLogic,
  WfConditionOp,
  WfFormFieldPerm,
  WfFormSchema,
  WfInitiatorScopeItem,
  WfModel,
  WfRejectAction,
  WfReturnPolicy,
  WfTimeout,
  WfWebhookFailureAction,
} from './schema'
import { serializeWfFormSchema, validateWfFormSchema } from './formSchema'
import { cloneModel, findNode } from './model'

export type WfConditionValueKind = 'text' | 'number' | 'list' | 'none'

export interface WfConditionOperatorMeta {
  op: WfConditionOp
  valueKind: WfConditionValueKind
}

export const WF_CONDITION_OPERATOR_META = [
  { op: 'eq', valueKind: 'text' },
  { op: 'ne', valueKind: 'text' },
  { op: 'gt', valueKind: 'number' },
  { op: 'gte', valueKind: 'number' },
  { op: 'lt', valueKind: 'number' },
  { op: 'lte', valueKind: 'number' },
  { op: 'in', valueKind: 'list' },
  { op: 'notIn', valueKind: 'list' },
  { op: 'contains', valueKind: 'text' },
  { op: 'empty', valueKind: 'none' },
  { op: 'notEmpty', valueKind: 'none' },
] as const satisfies readonly WfConditionOperatorMeta[]

const conditionValueKinds = Object.fromEntries(
  WF_CONDITION_OPERATOR_META.map(({ op, valueKind }) => [op, valueKind]),
) as Readonly<Record<WfConditionOp, WfConditionValueKind>>

export function isWfConditionOp(value: unknown): value is WfConditionOp {
  return typeof value === 'string' && Object.hasOwn(conditionValueKinds, value)
}

export function classifyConditionOp(op: WfConditionOp): WfConditionValueKind {
  return conditionValueKinds[op]
}

interface WfEditorNodeConfigBase {
  name: string
}

export const WF_WEBHOOK_METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD'] as const
export type WfWebhookMethod = typeof WF_WEBHOOK_METHODS[number]

export function isWfWebhookUrl(value: string): boolean {
  try {
    const protocol = new URL(value).protocol
    return protocol === 'http:' || protocol === 'https:'
  } catch {
    return false
  }
}

export type WfEditorNodeConfig =
  | (WfEditorNodeConfigBase & {
    type: 'start'
    formComponent: string | null
    formSchema?: WfFormSchema | null
    initiatorScope: WfInitiatorScopeItem[]
  })
  | (WfEditorNodeConfigBase & {
    type: 'branch'
    armExpressions: Readonly<Record<string, WfConditionExpr>>
  })
  | (WfEditorNodeConfigBase & {
    type: 'parallel'
  })
  | (WfEditorNodeConfigBase & {
    type: 'approval'
    assignee: WfAssignee
    mode: WfApprovalMode
    allPassRatio?: number
    returnPolicy: WfReturnPolicy
    returnToNodeId?: string
    onReject: WfRejectAction
    rejectToNodeId?: string
    timeout?: WfTimeout
    buttonLabels?: WfButtonLabels
    formPerms?: WfFormFieldPerm[]
  })
  | (WfEditorNodeConfigBase & {
    type: 'cc'
    assignee: WfAssignee
  })
  | (WfEditorNodeConfigBase & {
    type: 'webhook'
    webhookUrl: string
    webhookMethod: WfWebhookMethod
    webhookHeaders: Record<string, string | null>
    webhookTimeoutSeconds: number
    webhookOnFailure: WfWebhookFailureAction
    maxAttempts: number
  })

export function createConditionGroup(logic: WfConditionLogic = 'and'): WfConditionExpr {
  return { logic, children: [] }
}

export function createConditionLeaf(): WfConditionExpr {
  return {
    field: '',
    op: 'eq',
    value: '',
    children: null,
  }
}

export function appendConditionChild(
  group: WfConditionExpr,
  child: WfConditionExpr,
): WfConditionExpr | null {
  if (group.children == null) return null
  return { ...group, children: [...group.children, child] }
}

export function replaceConditionChild(
  group: WfConditionExpr,
  index: number,
  replacement: WfConditionExpr,
): WfConditionExpr | null {
  if (group.children == null || index < 0 || index >= group.children.length) return null
  const children = [...group.children]
  children[index] = replacement
  return { ...group, children }
}

export function removeConditionChild(group: WfConditionExpr, index: number): WfConditionExpr | null {
  if (group.children == null || index < 0 || index >= group.children.length) return null
  return {
    ...group,
    children: group.children.filter((_, childIndex) => childIndex !== index),
  }
}

export function setConditionOp(leaf: WfConditionExpr, op: WfConditionOp): WfConditionExpr {
  const { value, ...base } = leaf
  const valueKind = classifyConditionOp(op)
  if (valueKind === 'none') return { ...base, op }
  if (valueKind === 'list') {
    return { ...base, op, value: Array.isArray(value) ? value.map(String) : [] }
  }
  if (valueKind === 'number') {
    return { ...base, op, value: typeof value === 'number' && Number.isFinite(value) ? value : 0 }
  }
  const text = typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean'
    ? String(value)
    : ''
  return { ...base, op, value: text }
}

export function applyNodeConfiguration(
  model: WfModel,
  nodeId: string,
  config: WfEditorNodeConfig,
): WfModel | null {
  const next = cloneModel(model)
  const node = findNode(next.root, nodeId)
  if (!node || node.type !== config.type) return null

  node.name = config.name.trim() || node.name
  if (config.type === 'start') {
    const formComponent = config.formComponent?.trim() || null
    const formSchema = serializeWfFormSchema(config.formSchema)
    if ((formComponent && formSchema) || (formSchema && validateWfFormSchema(formSchema).length)) return null
    next.formComponent = formComponent
    next.formSchema = formSchema
    node.props = {
      ...node.props,
      initiatorScope: JSON.parse(JSON.stringify(config.initiatorScope)) as WfInitiatorScopeItem[],
    }
    return next
  }
  if (config.type === 'branch') {
    const expressions = config.armExpressions
    for (const armId of Object.keys(expressions)) {
      const arm = node.conditions?.find((candidate) => candidate.id === armId)
      if (!arm) return null
      if (!arm.isDefault) {
        const expression = expressions[armId]
        if (!expression || expression.children == null) return null
        arm.expr = JSON.parse(JSON.stringify(expression)) as WfConditionExpr
      }
    }
    return next
  }
  if (config.type === 'parallel') return next
  if (config.type === 'cc') {
    node.props = {
      ...node.props,
      assignee: JSON.parse(JSON.stringify(config.assignee)) as WfAssignee,
    }
    return next
  }
  if (config.type === 'webhook') {
    if (
      !isWfWebhookUrl(config.webhookUrl)
      || !WF_WEBHOOK_METHODS.includes(config.webhookMethod)
      || !Number.isInteger(config.webhookTimeoutSeconds)
      || config.webhookTimeoutSeconds < 1
      || config.webhookTimeoutSeconds > 120
      || !['fail', 'manual'].includes(config.webhookOnFailure)
      || !Number.isInteger(config.maxAttempts)
      || config.maxAttempts < 1
      || config.maxAttempts > 100
    ) return null
    node.props = {
      ...node.props,
      webhookUrl: config.webhookUrl.trim(),
      webhookMethod: config.webhookMethod,
      webhookHeaders: JSON.parse(JSON.stringify(config.webhookHeaders)) as Record<string, string | null>,
      webhookTimeoutSeconds: config.webhookTimeoutSeconds,
      webhookOnFailure: config.webhookOnFailure,
      maxAttempts: config.maxAttempts,
    }
    return next
  }

  const mode = config.assignee.provider === 'multiLeader' ? 'seq' : config.mode
  const allPassRatio = config.allPassRatio ?? 100
  if (
    (mode === 'all' && (!Number.isInteger(allPassRatio) || allPassRatio < 1 || allPassRatio > 100))
    || (mode === 'seq' && allPassRatio !== 100)
  ) return null

  node.props = {
    ...node.props,
    assignee: JSON.parse(JSON.stringify(config.assignee)) as WfAssignee,
    mode,
    allPassRatio: mode === 'any' ? undefined : allPassRatio,
    nobody: 'autoPass',
    nobodyTransferUserId: undefined,
    onReject: config.onReject,
    rejectToNodeId: config.onReject === 'toNode' ? config.rejectToNodeId : undefined,
    returnPolicy: config.returnPolicy,
    returnToNodeId: config.returnPolicy === 'node' ? config.returnToNodeId : undefined,
      timeout: config.timeout && config.timeout.hours > 0
      ? JSON.parse(JSON.stringify(config.timeout)) as WfTimeout
      : undefined,
      buttonLabels: compactButtonLabels(config.buttonLabels),
      ...(config.formPerms ? { formPerms: JSON.parse(JSON.stringify(config.formPerms)) as WfFormFieldPerm[] } : {}),
  }
  return next
}

function compactButtonLabels(labels: WfButtonLabels | undefined): WfButtonLabels | undefined {
  if (!labels) return undefined
  const next: WfButtonLabels = {}
  for (const key of ['approve', 'reject', 'return', 'transfer', 'delegate', 'urge'] as const) {
    const value = labels[key]?.trim()
    if (value) next[key] = value
  }
  return Object.keys(next).length > 0 ? next : undefined
}
