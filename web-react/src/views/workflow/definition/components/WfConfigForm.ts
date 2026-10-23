import type { WfId } from '@/workflow/id'
import type {
  WfApprovalMode,
  WfConditionExpr,
  WfFormPermAccess,
  WfFormSchema,
  WfRejectAction,
  WfReturnPolicy,
  WfTimeoutAction,
  WfWebhookFailureAction,
} from '@/workflow/schema'
import type { WfWebhookMethod } from '@/workflow/configuration'

export type WfFormMode = 'none' | 'schema' | 'component'

export interface WfConfigDrawerForm {
  name: string
  formComponent: string
  formMode: WfFormMode
  formSchema: WfFormSchema | null
  formPerms: Record<string, WfFormPermAccess>
  provider: string
  mode: WfApprovalMode
  allPassRatio: number
  level: number
  userIds: WfId[]
  roleId: WfId | null
  positionId: WfId | null
  positionOrgId: WfId | null
  initiatorUserIds: WfId[]
  initiatorRoleIds: WfId[]
  initiatorOrgIds: WfId[]
  armExpressions: Record<string, WfConditionExpr>
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
  webhookUrl: string
  webhookMethod: WfWebhookMethod
  webhookHeaders: Array<{ key: string; value: string }>
  webhookTimeoutSeconds: number
  webhookOnFailure: WfWebhookFailureAction
  maxAttempts: number
}
