import type { WfId } from '@/workflow/id'
import { normalizeWfId } from '@/workflow/id'
import { createConditionGroup, type WfEditorNodeConfig, type WfWebhookMethod } from '@/workflow/configuration'
import { serializeWfFormSchema } from '@/workflow/formSchema'
import type {
  WfApprovalMode,
  WfConditionExpr,
  WfFormPermAccess,
  WfFormSchema,
  WfModel,
  WfNode,
  WfRejectAction,
  WfReturnPolicy,
  WfTimeoutAction,
  WfWebhookFailureAction,
} from '@/workflow/schema'

export interface WfStartFieldsState {
  formComponent: string
  formMode: 'none' | 'schema' | 'component'
  formSchema: WfFormSchema | null
  initiatorUserIds: WfId[]
  initiatorRoleIds: WfId[]
  initiatorOrgIds: WfId[]
}

export interface WfAssigneeFieldsState {
  provider: string
  level: number
  userIds: WfId[]
  roleId: WfId | null
  positionId: WfId | null
  positionOrgId: WfId | null
}

export interface WfApprovalAdvancedState {
  mode: WfApprovalMode
  allPassRatio: number | null
  formPerms: Record<string, WfFormPermAccess>
  returnPolicy: WfReturnPolicy
  returnToNodeId: string | null
  onReject: WfRejectAction
  rejectToNodeId: string | null
  timeoutHours: number
  timeoutAction: WfTimeoutAction
  timeoutTransferUserId: WfId | null
  labelApprove: string
  labelReject: string
  labelReturn: string
  labelTransfer: string
  labelDelegate: string
  labelUrge: string
}

export interface WfWebhookFieldsState {
  url: string
  method: WfWebhookMethod
  headers: Array<{ key: string; value: string }>
  timeoutSeconds: number | null
  onFailure: WfWebhookFailureAction
  maxAttempts: number | null
}

export interface WfDrawerFormState {
  name: string
  start: WfStartFieldsState
  assignee: WfAssigneeFieldsState
  approval: WfApprovalAdvancedState
  armExpressions: Record<string, WfConditionExpr>
  webhook: WfWebhookFieldsState
}

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
}

function loadArmExpression(expression: WfConditionExpr | null | undefined): WfConditionExpr {
  const value = clone(expression ?? createConditionGroup())
  return value.children != null ? value : { ...createConditionGroup(), children: [value] }
}

export function loadDrawerForm(model: WfModel, node: WfNode): WfDrawerFormState {
  const schema = model.formSchema ? clone(model.formSchema) : null
  const assignee = node.props?.assignee
  const params = assignee?.params ?? {}
  const ids = params.userIds
  const userIds = Array.isArray(ids) ? ids : params.userId == null ? [] : [params.userId]
  const scope = node.props?.initiatorScope ?? []
  const method = (node.props?.webhookMethod ?? 'POST').toUpperCase() as WfWebhookMethod

  return {
    name: node.name,
    start: {
      formComponent: model.formComponent ?? '',
      formMode: schema ? 'schema' : model.formComponent ? 'component' : 'none',
      formSchema: schema,
      initiatorUserIds: scope.filter((item) => item.type === 'user').map((item) => item.id),
      initiatorRoleIds: scope.filter((item) => item.type === 'role').map((item) => item.id),
      initiatorOrgIds: scope.filter((item) => item.type === 'org').map((item) => item.id),
    },
    assignee: {
      provider: assignee?.provider || (node.type === 'cc' ? 'user' : 'leader'),
      level: Number(params.level ?? 1) || 1,
      userIds: userIds.map(normalizeWfId).filter((id): id is WfId => id != null),
      roleId: normalizeWfId(params.roleId ?? (Array.isArray(params.roleIds) ? params.roleIds[0] : null)),
      positionId: normalizeWfId(params.positionId),
      positionOrgId: normalizeWfId(params.orgId),
    },
    approval: {
      mode: assignee?.provider === 'multiLeader' ? 'seq' : node.props?.mode ?? 'any',
      allPassRatio: node.props?.allPassRatio ?? 100,
      formPerms: Object.fromEntries([
        ...(schema?.fields ?? []).map((field) => [field.key, 'editable'] as const),
        ...(node.props?.formPerms ?? [])
          .filter((permission) => Boolean(permission?.field))
          .map((permission) => [permission.field, permission.access ?? 'editable'] as const),
      ]),
      returnPolicy: node.props?.returnPolicy ?? 'prev',
      returnToNodeId: node.props?.returnToNodeId ?? null,
      onReject: node.props?.onReject ?? 'terminate',
      rejectToNodeId: node.props?.rejectToNodeId ?? null,
      timeoutHours: node.props?.timeout?.hours ?? 0,
      timeoutAction: node.props?.timeout?.action ?? 'remind',
      timeoutTransferUserId: normalizeWfId(node.props?.timeout?.transferUserId),
      labelApprove: node.props?.buttonLabels?.approve ?? '',
      labelReject: node.props?.buttonLabels?.reject ?? '',
      labelReturn: node.props?.buttonLabels?.return ?? '',
      labelTransfer: node.props?.buttonLabels?.transfer ?? '',
      labelDelegate: node.props?.buttonLabels?.delegate ?? '',
      labelUrge: node.props?.buttonLabels?.urge ?? '',
    },
    armExpressions: Object.fromEntries(
      (node.conditions ?? []).filter((arm) => !arm.isDefault).map((arm) => [arm.id, loadArmExpression(arm.expr)]),
    ),
    webhook: {
      url: node.props?.webhookUrl ?? '',
      method,
      headers: Object.entries(node.props?.webhookHeaders ?? {}).map(([key, value]) => ({ key, value: value ?? '' })),
      timeoutSeconds: node.props?.webhookTimeoutSeconds ?? 30,
      onFailure: node.props?.webhookOnFailure ?? 'fail',
      maxAttempts: node.props?.maxAttempts ?? 3,
    },
  }
}

export function buildAssigneeParams(form: WfAssigneeFieldsState): Record<string, unknown> {
  if (form.provider === 'user') return { userIds: [...form.userIds] }
  if (form.provider === 'leader' || form.provider === 'multiLeader') return { level: form.level }
  if (form.provider === 'role') return form.roleId ? { roleId: form.roleId } : {}
  if (form.provider === 'position') {
    return form.positionId
      ? { positionId: form.positionId, ...(form.positionOrgId ? { orgId: form.positionOrgId } : {}) }
      : {}
  }
  return {}
}

export function buildDrawerNodeConfig(node: WfNode, form: WfDrawerFormState): WfEditorNodeConfig | null {
  if (node.type === 'start') {
    return {
      type: 'start',
      name: form.name,
      formComponent: form.start.formMode === 'component' ? form.start.formComponent : null,
      formSchema: form.start.formMode === 'schema' ? serializeWfFormSchema(form.start.formSchema) : null,
      initiatorScope: [
        ...form.start.initiatorUserIds.map((id) => ({ type: 'user' as const, id })),
        ...form.start.initiatorRoleIds.map((id) => ({ type: 'role' as const, id })),
        ...form.start.initiatorOrgIds.map((id) => ({ type: 'org' as const, id })),
      ],
    }
  }
  if (node.type === 'branch') return { type: 'branch', name: form.name, armExpressions: form.armExpressions }
  if (node.type === 'parallel') return { type: 'parallel', name: form.name }
  const assignee = { provider: form.assignee.provider, params: buildAssigneeParams(form.assignee) }
  if (node.type === 'cc') return { type: 'cc', name: form.name, assignee }
  if (node.type === 'webhook') {
    if (form.webhook.timeoutSeconds == null || form.webhook.maxAttempts == null) return null
    return {
      type: 'webhook',
      name: form.name,
      webhookUrl: form.webhook.url,
      webhookMethod: form.webhook.method,
      webhookHeaders: Object.fromEntries(
        form.webhook.headers.filter(({ key }) => key.trim()).map(({ key, value }) => [key.trim(), value]),
      ),
      webhookTimeoutSeconds: form.webhook.timeoutSeconds,
      webhookOnFailure: form.webhook.onFailure,
      maxAttempts: form.webhook.maxAttempts,
    }
  }
  if (node.type !== 'approval') return null

  const mode = form.assignee.provider === 'multiLeader' ? 'seq' : form.approval.mode
  const allPassRatio = form.approval.allPassRatio
  let savedAllPassRatio: number | undefined
  if (mode === 'all') {
    if (allPassRatio == null) return null
    savedAllPassRatio = allPassRatio
  } else if (mode === 'seq') {
    savedAllPassRatio = 100
  }
  const fields = form.start.formComponent.trim() ? [] : form.start.formSchema?.fields ?? []
  return {
    type: 'approval',
    name: form.name,
    assignee,
    mode,
    allPassRatio: savedAllPassRatio,
    returnPolicy: form.approval.returnPolicy,
    returnToNodeId: form.approval.returnToNodeId ?? undefined,
    onReject: form.approval.onReject,
    rejectToNodeId: form.approval.rejectToNodeId ?? undefined,
    timeout: form.approval.timeoutHours > 0
      ? {
          hours: form.approval.timeoutHours,
          action: form.approval.timeoutAction,
          transferUserId: form.approval.timeoutAction === 'transfer'
            ? form.approval.timeoutTransferUserId ?? undefined
            : undefined,
        }
      : undefined,
    buttonLabels: {
      approve: form.approval.labelApprove,
      reject: form.approval.labelReject,
      return: form.approval.labelReturn,
      transfer: form.approval.labelTransfer,
      delegate: form.approval.labelDelegate,
      urge: form.approval.labelUrge,
    },
    ...(fields.length
      ? {
          formPerms: fields
            .map((field) => ({ field: field.key, access: form.approval.formPerms[field.key] ?? 'editable' }))
            .filter(({ access }) => access !== 'editable'),
        }
      : {}),
  }
}
