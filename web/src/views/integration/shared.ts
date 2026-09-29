// 第三方接入管理页共用:按钮权限码(= 后端规范化路由,路由参数名小写)、时间格式化、枚举文案、深链参数、状态语义色。
import type { LocationQuery } from 'vue-router'
import {
  CredentialStatus,
  DeliveryActions,
  DeliveryAttemptKind,
  DeliveryAttemptTrigger,
  DeliveryStatus,
  InboundOutcome,
  OutboundAuthType,
  OutboundOutcome,
  type DeliveryAction,
} from '@/types/integration'

export const PERM = {
  appPage: 'GET:/api/v1/integration/app/page',
  appAdd: 'POST:/api/v1/integration/app',
  appUpdate: 'PUT:/api/v1/integration/app/{id}',
  appEnable: 'POST:/api/v1/integration/app/{id}/enable',
  appDisable: 'POST:/api/v1/integration/app/{id}/disable',
  appDelete: 'DELETE:/api/v1/integration/app/{id}',
  credList: 'GET:/api/v1/integration/app/{id}/credentials',
  credCreate: 'POST:/api/v1/integration/app/{id}/credentials',
  credRotate: 'POST:/api/v1/integration/app/{id}/credentials/{credentialid}/rotate',
  credRevoke: 'POST:/api/v1/integration/app/{id}/credentials/{credentialid}/revoke',
  credDelete: 'DELETE:/api/v1/integration/app/{id}/credentials/{credentialid}',
  credExpiry: 'PUT:/api/v1/integration/app/{id}/credentials/{credentialid}/expiry',
  grantGet: 'GET:/api/v1/integration/app/{id}/grants',
  grantSet: 'PUT:/api/v1/integration/app/{id}/grants',
  scopeGet: 'GET:/api/v1/integration/app/{id}/scopes',
  scopeSet: 'PUT:/api/v1/integration/app/{id}/scopes',
  catalogEndpoints: 'GET:/api/v1/integration/catalog/endpoints',
  catalogScopes: 'GET:/api/v1/integration/catalog/scopes',
  scopeOptions: 'GET:/api/v1/integration/catalog/scopes/{key}/options',
  openApiDoc: 'GET:/api/v1/integration/catalog/open-api/{version}',
  inboundLog: 'GET:/api/v1/integration/inbound-log/page',
  outboundLog: 'GET:/api/v1/integration/outbound-log/page',
  outboundTargets: 'GET:/api/v1/integration/outbound-log/targets',
  deliverySummary: 'GET:/api/v1/integration/delivery/summary',
  deliveryGet: 'GET:/api/v1/integration/delivery/{id}',
} as const

/** 人工操作 → 按钮权限码(按钮先按权限显示,再按服务端 allowedActions 决定可否点)。 */
export const DELIVERY_ACTION_PERM: Record<DeliveryAction, string> = {
  [DeliveryActions.Retry]: 'POST:/api/v1/integration/delivery/{id}/retry',
  [DeliveryActions.Query]: 'POST:/api/v1/integration/delivery/{id}/query',
  [DeliveryActions.ConfirmSucceeded]: 'POST:/api/v1/integration/delivery/{id}/confirm-succeeded',
  [DeliveryActions.ConfirmNotExecuted]: 'POST:/api/v1/integration/delivery/{id}/confirm-not-executed',
  [DeliveryActions.Cancel]: 'POST:/api/v1/integration/delivery/{id}/cancel',
}

const pad = (n: number) => String(n).padStart(2, '0')

/** 后端时间(ISO 8601,带偏移或本地) → 本地「YYYY-MM-DD HH:mm:ss」;空值 —。 */
export function fmtDateTime(v?: string | null): string {
  if (!v) return '—'
  const d = new Date(v)
  if (Number.isNaN(d.getTime())) return v
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
}

/** 枚举值 → 语言包键 → 文案(键表与状态色表通用);空值或未收录的值显示 —,不把 — 当键去翻译。 */
export function enumLabel(t: (key: string) => string, map: Record<number, string | { key: string }>, value?: number | null): string {
  const entry = value == null ? undefined : map[Number(value)]
  const key = typeof entry === 'string' ? entry : entry?.key
  return key ? t(key) : '—'
}

/** 深链筛选参数:只认非空的单值查询参数,缺省、空串或重复给出都当没有。 */
export function queryText(query: LocationQuery, key: string): string | null {
  const v = query[key]
  return typeof v === 'string' && v ? v : null
}

export type TagType = 'default' | 'success' | 'warning' | 'error' | 'info'

export const credentialStatusTag: Record<number, { key: string; type: TagType }> = {
  [CredentialStatus.Active]: { key: 'integration.credential.statusActive', type: 'success' },
  [CredentialStatus.Expired]: { key: 'integration.credential.statusExpired', type: 'warning' },
  [CredentialStatus.Revoked]: { key: 'integration.credential.statusRevoked', type: 'error' },
}

export const inboundOutcomeTag: Record<number, { key: string; type: TagType }> = {
  [InboundOutcome.Succeeded]: { key: 'integration.inboundLog.outcomeSucceeded', type: 'success' },
  [InboundOutcome.BusinessFailed]: { key: 'integration.inboundLog.outcomeBusinessFailed', type: 'warning' },
  [InboundOutcome.Unauthorized]: { key: 'integration.inboundLog.outcomeUnauthorized', type: 'error' },
  [InboundOutcome.Forbidden]: { key: 'integration.inboundLog.outcomeForbidden', type: 'error' },
  [InboundOutcome.RateLimited]: { key: 'integration.inboundLog.outcomeRateLimited', type: 'warning' },
  [InboundOutcome.InvalidRequest]: { key: 'integration.inboundLog.outcomeInvalidRequest', type: 'warning' },
  [InboundOutcome.Error]: { key: 'integration.inboundLog.outcomeError', type: 'error' },
}

/** HTTP 方法语义色(授权清单里一眼区分读写)。 */
export function methodTagType(method?: string | null): TagType {
  switch ((method ?? '').toUpperCase()) {
    case 'GET':
      return 'info'
    case 'DELETE':
      return 'error'
    case 'POST':
    case 'PUT':
    case 'PATCH':
      return 'warning'
    default:
      return 'default'
  }
}

/** 出站调用结果:受理(蓝)≠ 成功(绿);结果未知(橙)表示对方可能已执行。 */
export const outboundOutcomeTag: Record<number, { key: string; type: TagType }> = {
  [OutboundOutcome.Succeeded]: { key: 'integration.outbound.outcomeSucceeded', type: 'success' },
  [OutboundOutcome.Accepted]: { key: 'integration.outbound.outcomeAccepted', type: 'info' },
  [OutboundOutcome.AuthenticationFailed]: { key: 'integration.outbound.outcomeAuthFailed', type: 'error' },
  [OutboundOutcome.Rejected]: { key: 'integration.outbound.outcomeRejected', type: 'error' },
  [OutboundOutcome.NotSent]: { key: 'integration.outbound.outcomeNotSent', type: 'warning' },
  [OutboundOutcome.Unknown]: { key: 'integration.outbound.outcomeUnknown', type: 'warning' },
  [OutboundOutcome.Cancelled]: { key: 'integration.outbound.outcomeCancelled', type: 'default' },
}

export const outboundAuthTypeKey: Record<number, string> = {
  [OutboundAuthType.None]: 'integration.outbound.authNone',
  [OutboundAuthType.Bearer]: 'integration.outbound.authBearer',
  [OutboundAuthType.Header]: 'integration.outbound.authHeader',
  [OutboundAuthType.Basic]: 'integration.outbound.authBasic',
}

/** 投递状态:待确认(已受理,蓝)、成功(绿)、待核对(结果未知,橙)、耗尽与失败(红)各自可辨。 */
export const deliveryStatusTag: Record<number, { key: string; type: TagType }> = {
  [DeliveryStatus.Pending]: { key: 'integration.delivery.statusPending', type: 'default' },
  [DeliveryStatus.Dispatching]: { key: 'integration.delivery.statusDispatching', type: 'info' },
  [DeliveryStatus.AwaitingConfirmation]: { key: 'integration.delivery.statusAwaiting', type: 'info' },
  [DeliveryStatus.Succeeded]: { key: 'integration.delivery.statusSucceeded', type: 'success' },
  [DeliveryStatus.NeedsReconciliation]: { key: 'integration.delivery.statusReconcile', type: 'warning' },
  [DeliveryStatus.Exhausted]: { key: 'integration.delivery.statusExhausted', type: 'error' },
  [DeliveryStatus.Failed]: { key: 'integration.delivery.statusFailed', type: 'error' },
  [DeliveryStatus.Cancelled]: { key: 'integration.delivery.statusCancelled', type: 'default' },
}

/** 每个状态对管理员的一句话说明(详情抽屉顶部)。 */
export const deliveryStatusHint: Partial<Record<number, string>> = {
  [DeliveryStatus.AwaitingConfirmation]: 'integration.delivery.hintAwaiting',
  [DeliveryStatus.NeedsReconciliation]: 'integration.delivery.hintReconcile',
  [DeliveryStatus.Exhausted]: 'integration.delivery.hintExhausted',
  [DeliveryStatus.Failed]: 'integration.delivery.hintFailed',
  [DeliveryStatus.Dispatching]: 'integration.delivery.hintDispatching',
}

export const attemptKindKey: Record<number, string> = {
  [DeliveryAttemptKind.Send]: 'integration.delivery.kindSend',
  [DeliveryAttemptKind.Query]: 'integration.delivery.kindQuery',
  [DeliveryAttemptKind.Recovery]: 'integration.delivery.kindRecovery',
  [DeliveryAttemptKind.Manual]: 'integration.delivery.kindManual',
  [DeliveryAttemptKind.Confirm]: 'integration.delivery.kindConfirm',
  [DeliveryAttemptKind.Expired]: 'integration.delivery.kindExpired',
}

export const attemptTriggerKey: Record<number, string> = {
  [DeliveryAttemptTrigger.Worker]: 'integration.delivery.triggerWorker',
  [DeliveryAttemptTrigger.Manual]: 'integration.delivery.triggerManual',
  [DeliveryAttemptTrigger.Callback]: 'integration.delivery.triggerCallback',
}

/** 人工操作 → 按钮与操作弹窗的文案键。 */
export const deliveryActionKey: Record<DeliveryAction, string> = {
  [DeliveryActions.Retry]: 'integration.delivery.actionRetry',
  [DeliveryActions.Query]: 'integration.delivery.actionQuery',
  [DeliveryActions.ConfirmSucceeded]: 'integration.delivery.actionConfirmSucceeded',
  [DeliveryActions.ConfirmNotExecuted]: 'integration.delivery.actionConfirmNotExecuted',
  [DeliveryActions.Cancel]: 'integration.delivery.actionCancel',
}

/** 尝试记录的结论文本(调用结果、对方状态、人工操作名、到期后的状态)→ 语言包键;未收录的原样显示。 */
export const attemptOutcomeKey: Record<string, string> = {
  Succeeded: 'integration.delivery.resultSucceeded',
  Accepted: 'integration.delivery.resultAccepted',
  AuthenticationFailed: 'integration.outbound.outcomeAuthFailed',
  Rejected: 'integration.outbound.outcomeRejected',
  NotSent: 'integration.outbound.outcomeNotSent',
  Unknown: 'integration.outbound.outcomeUnknown',
  Cancelled: 'integration.outbound.outcomeCancelled',
  Failed: 'integration.delivery.resultFailed',
  NotFound: 'integration.delivery.resultNotFound',
  NeedsReconciliation: 'integration.delivery.statusReconcile',
  Exhausted: 'integration.delivery.statusExhausted',
  lease_expired: 'integration.delivery.resultLeaseExpired',
  ...deliveryActionKey,
}
