/** 第三方接入协议 DTO:只从真实后端生成的 OpenAPI 契约取型。 */
import type { components } from '@/api/schema'

type Schemas = components['schemas']

export type IntegrationAppRow = Schemas['IntegrationAppListItem']
export type IntegrationAppCreateInput = Schemas['IntegrationAppCreateInput']
export type IntegrationAppUpdateInput = Schemas['IntegrationAppUpdateInput']
export type OpenAppCredential = Schemas['OpenAppCredentialView']
export type OpenAppCredentialIssued = Schemas['OpenAppCredentialIssued']
export type OpenAppCredentialCreateInput = Schemas['OpenAppCredentialCreateInput']
export type OpenAppCredentialRotateInput = Schemas['OpenAppCredentialRotateInput']
export type OpenAppCredentialExpiryInput = Schemas['OpenAppCredentialExpiryInput']
export type OpenAppGrantView = Schemas['OpenAppGrantView']
export type OpenAppScopeBinding = Schemas['OpenAppScopeBindingView']
export type OpenAppScopeBindingInput = Schemas['OpenAppScopeBindingInput']
export type OpenApiEndpoint = Schemas['OpenApiEndpointInfo']
export type OpenApiScopePolicy = Schemas['OpenApiScopePolicyInfo']
export type OpenApiScopeOption = Schemas['OpenApiScopeOption']
export type InboundLogRow = Schemas['InboundLogView']
export type OutboundLogRow = Schemas['OutboundLogView']
export type OutboundTarget = Schemas['OutboundTargetView']
export type DeliveryRow = Schemas['DeliveryListItem']
export type DeliveryDetail = Schemas['DeliveryDetail']
export type DeliveryAttempt = Schemas['DeliveryAttemptView']
export type DeliveryStatusCount = Schemas['DeliveryStatusCount']
export type DeliveryActionInput = Schemas['DeliveryActionInput']

/** 凭据状态(与后端 OpenAppCredentialStatus 数值一致)。 */
export const CredentialStatus = { Active: 0, Expired: 1, Revoked: 2 } as const

/** 开放调用结果(与后端 InboundCallOutcome 数值一致)。 */
export const InboundOutcome = {
  Succeeded: 0,
  BusinessFailed: 1,
  Unauthorized: 2,
  Forbidden: 3,
  RateLimited: 4,
  InvalidRequest: 5,
  Error: 6,
} as const

/** 内置范围键:none 不需要绑定,不出现在范围配置里。 */
export const SCOPE_NONE = 'none'
export const SCOPE_ORG = 'org'

/** 出站调用结果分类(与后端 OutboundOutcome 数值一致)。受理不等于成功;结果未知表示对方可能已执行。 */
export const OutboundOutcome = {
  Succeeded: 0,
  Accepted: 1,
  AuthenticationFailed: 2,
  Rejected: 3,
  NotSent: 4,
  Unknown: 5,
  Cancelled: 6,
} as const

/** 出站凭据施加方式(与后端 OutboundAuthType 数值一致)。 */
export const OutboundAuthType = { None: 0, Bearer: 1, Header: 2, Basic: 3 } as const

/** 可靠投递状态(与后端 DeliveryStatus 数值一致)。 */
export const DeliveryStatus = {
  Pending: 0,
  Dispatching: 1,
  AwaitingConfirmation: 2,
  Succeeded: 3,
  NeedsReconciliation: 4,
  Exhausted: 5,
  Failed: 6,
  Cancelled: 7,
} as const

/** 投递尝试种类与触发方(与后端枚举数值一致)。 */
export const DeliveryAttemptKind = { Send: 0, Query: 1, Recovery: 2, Manual: 3, Confirm: 4, Expired: 5 } as const
export const DeliveryAttemptTrigger = { Worker: 0, Manual: 1, Callback: 2 } as const

/** 人工操作名(与后端 DeliveryActions / 管理接口路由末段一致)。可用性一律以服务端返回的 allowedActions 为准。 */
export const DeliveryActions = {
  Retry: 'retry',
  Query: 'query',
  ConfirmSucceeded: 'confirm-succeeded',
  ConfirmNotExecuted: 'confirm-not-executed',
  Cancel: 'cancel',
} as const
export type DeliveryAction = (typeof DeliveryActions)[keyof typeof DeliveryActions]
