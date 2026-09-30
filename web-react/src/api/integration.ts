/** 第三方接入管理 API:路径与 DTO 均来自真实后端生成的 schema.d.ts(React 模板自有副本)。 */
import { client } from './client'
import { ApiError, pageParams, toPage, unwrap } from './index'
import {
  DeliveryActions,
  type DeliveryAction,
  type DeliveryActionInput,
  type DeliveryDetail,
  type DeliveryRow,
  type DeliveryStatusCount,
  type OutboundLogRow,
  type OutboundTarget,
} from '@/types/integration'
import type {
  InboundLogRow,
  IntegrationAppCreateInput,
  IntegrationAppRow,
  IntegrationAppUpdateInput,
  OpenApiEndpoint,
  OpenApiScopeOption,
  OpenApiScopePolicy,
  OpenAppCredential,
  OpenAppCredentialCreateInput,
  OpenAppCredentialExpiryInput,
  OpenAppCredentialIssued,
  OpenAppCredentialRotateInput,
  OpenAppGrantView,
  OpenAppScopeBinding,
  OpenAppScopeBindingInput,
} from '@/types/integration'

type Id = number | string

export const integrationAppApi = {
  page: (params: { page: number; pageSize: number; keyword?: string; enabled?: boolean }) =>
    client
      .GET('/api/v1/integration/app/page', {
        params: { query: { ...pageParams(params), Keyword: params.keyword || undefined, Enabled: params.enabled } },
      })
      .then((r) => toPage<IntegrationAppRow>(r)),

  add: (body: IntegrationAppCreateInput) =>
    client.POST('/api/v1/integration/app', { body }).then((r) => unwrap<number | string>(r)),

  update: (id: Id, body: IntegrationAppUpdateInput) =>
    client.PUT('/api/v1/integration/app/{id}', { params: { path: { id } }, body }).then((r) => unwrap<boolean>(r)),

  setEnabled: (id: Id, enabled: boolean) =>
    (enabled
      ? client.POST('/api/v1/integration/app/{id}/enable', { params: { path: { id } } })
      : client.POST('/api/v1/integration/app/{id}/disable', { params: { path: { id } } })
    ).then((r) => unwrap<boolean>(r)),

  remove: (id: Id) =>
    client.DELETE('/api/v1/integration/app/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),

  credentials: (id: Id) =>
    client
      .GET('/api/v1/integration/app/{id}/credentials', { params: { path: { id } } })
      .then((r) => unwrap<OpenAppCredential[]>(r)),

  /** 发放凭据:返回值含完整凭据原文,调用方只能在当次交互里展示,不得写入任何存储或日志。 */
  createCredential: (id: Id, body: OpenAppCredentialCreateInput) =>
    client
      .POST('/api/v1/integration/app/{id}/credentials', { params: { path: { id } }, body })
      .then((r) => unwrap<OpenAppCredentialIssued>(r)),

  /** 轮换凭据:同上,新凭据原文只返回一次。 */
  rotateCredential: (id: Id, credentialId: Id, body: OpenAppCredentialRotateInput) =>
    client
      .POST('/api/v1/integration/app/{id}/credentials/{credentialId}/rotate', {
        params: { path: { id, credentialId } },
        body,
      })
      .then((r) => unwrap<OpenAppCredentialIssued>(r)),

  revokeCredential: (id: Id, credentialId: Id) =>
    client
      .POST('/api/v1/integration/app/{id}/credentials/{credentialId}/revoke', { params: { path: { id, credentialId } } })
      .then((r) => unwrap<boolean>(r)),

  deleteCredential: (id: Id, credentialId: Id) =>
    client
      .DELETE('/api/v1/integration/app/{id}/credentials/{credentialId}', { params: { path: { id, credentialId } } })
      .then((r) => unwrap<boolean>(r)),

  setCredentialExpiry: (id: Id, credentialId: Id, body: OpenAppCredentialExpiryInput) =>
    client
      .PUT('/api/v1/integration/app/{id}/credentials/{credentialId}/expiry', { params: { path: { id, credentialId } }, body })
      .then((r) => unwrap<boolean>(r)),

  grants: (id: Id) =>
    client.GET('/api/v1/integration/app/{id}/grants', { params: { path: { id } } }).then((r) => unwrap<OpenAppGrantView>(r)),

  setGrants: (id: Id, permissions: string[]) =>
    client
      .PUT('/api/v1/integration/app/{id}/grants', { params: { path: { id } }, body: { permissions } })
      .then((r) => unwrap<boolean>(r)),

  scopes: (id: Id) =>
    client.GET('/api/v1/integration/app/{id}/scopes', { params: { path: { id } } }).then((r) => unwrap<OpenAppScopeBinding[]>(r)),

  setScopes: (id: Id, bindings: OpenAppScopeBindingInput[]) =>
    client
      .PUT('/api/v1/integration/app/{id}/scopes', { params: { path: { id } }, body: { bindings } })
      .then((r) => unwrap<boolean>(r)),
}

export const integrationCatalogApi = {
  endpoints: () => client.GET('/api/v1/integration/catalog/endpoints', {}).then((r) => unwrap<OpenApiEndpoint[]>(r)),

  scopes: () => client.GET('/api/v1/integration/catalog/scopes', {}).then((r) => unwrap<OpenApiScopePolicy[]>(r)),

  scopeOptions: (key: string, keyword?: string) =>
    client
      .GET('/api/v1/integration/catalog/scopes/{key}/options', {
        params: { path: { key }, query: { keyword: keyword || undefined } },
      })
      .then((r) => unwrap<OpenApiScopeOption[]>(r)),

  /** 下载开放接口文档(JSON 文件);业务失败时后端回 JSON 信封(非 2xx 也是),这里按信封的业务码抛错。 */
  openApiDocument: async (version: string): Promise<Blob> => {
    const res = await client.GET('/api/v1/integration/catalog/open-api/{version}', {
      params: { path: { version } },
      parseAs: 'blob',
    })
    type Envelope = { code?: number; msgKey?: string; args?: Record<string, unknown>; message?: string; openapi?: string }
    if (!res.response.ok) {
      // 同内核下载的失败口径:非 2xx 优先用信封里的业务码(如 41001 无权限),没有信封才退回 HTTP 状态
      const env = (res.error ?? {}) as Envelope
      if (typeof env.code === 'number') throw new ApiError(env.code, env.msgKey, env.args, env.message)
      throw new ApiError(res.response.status, undefined, undefined, res.response.statusText)
    }
    const blob = res.data as Blob
    const text = await blob.text()
    const parsed = JSON.parse(text) as Envelope
    if (typeof parsed.code === 'number' && parsed.openapi === undefined)
      throw new ApiError(parsed.code, parsed.msgKey, parsed.args, parsed.message)
    return new Blob([text], { type: 'application/json' })
  },
}

/** 时间范围筛选 → 后端本地时间口径的起止(开放调用记录、出站记录、投递共用)。 */
const rangeQuery = (range?: [string, string] | null) => ({
  StartTime: range?.[0] ? `${range[0]} 00:00:00` : undefined,
  EndTime: range?.[1] ? `${range[1]} 23:59:59` : undefined,
})

export const integrationInboundLogApi = {
  page: (params: {
    page: number
    pageSize: number
    appId?: Id
    route?: string
    outcome?: number
    traceId?: string
    clientRequestId?: string
    createTime?: [string, string] | null
  }) =>
    client
      .GET('/api/v1/integration/inbound-log/page', {
        params: {
          query: {
            ...pageParams(params),
            AppId: params.appId ?? undefined,
            Route: params.route || undefined,
            Outcome: params.outcome ?? undefined,
            TraceId: params.traceId || undefined,
            ClientRequestId: params.clientRequestId || undefined,
            ...rangeQuery(params.createTime),
          },
        },
      })
      .then((r) => toPage<InboundLogRow>(r)),
}

export const integrationOutboundLogApi = {
  page: (params: {
    page: number
    pageSize: number
    target?: string
    operation?: string
    outcome?: number
    deliveryId?: Id
    callId?: string
    traceId?: string
    createTime?: [string, string] | null
  }) =>
    client
      .GET('/api/v1/integration/outbound-log/page', {
        params: {
          query: {
            ...pageParams(params),
            Target: params.target || undefined,
            Operation: params.operation || undefined,
            Outcome: params.outcome ?? undefined,
            DeliveryId: params.deliveryId ?? undefined,
            CallId: params.callId || undefined,
            TraceId: params.traceId || undefined,
            ...rangeQuery(params.createTime),
          },
        },
      })
      .then((r) => toPage<OutboundLogRow>(r)),

  /** 已配置的出站目标:只回答「能否取到秘密」,永不回显秘密。 */
  targets: () => client.GET('/api/v1/integration/outbound-log/targets', {}).then((r) => unwrap<OutboundTarget[]>(r)),
}

export const integrationDeliveryApi = {
  page: (params: {
    page: number
    pageSize: number
    status?: number
    adapter?: string
    operation?: string
    deliveryKey?: string
    businessKey?: string
    createTime?: [string, string] | null
  }) =>
    client
      .GET('/api/v1/integration/delivery/page', {
        params: {
          query: {
            ...pageParams(params),
            Status: params.status ?? undefined,
            Adapter: params.adapter || undefined,
            Operation: params.operation || undefined,
            DeliveryKey: params.deliveryKey || undefined,
            BusinessKey: params.businessKey || undefined,
            ...rangeQuery(params.createTime),
          },
        },
      })
      .then((r) => toPage<DeliveryRow>(r)),

  summary: () => client.GET('/api/v1/integration/delivery/summary', {}).then((r) => unwrap<DeliveryStatusCount[]>(r)),

  get: (id: Id) =>
    client.GET('/api/v1/integration/delivery/{id}', { params: { path: { id } } }).then((r) => unwrap<DeliveryDetail>(r)),

  /** 执行人工操作;服务端复检可用性(49045)、页面栅栏(49046)与确认说明(49047),返回最新详情。 */
  act: (id: Id, action: DeliveryAction, body: DeliveryActionInput) => {
    const path = { params: { path: { id } }, body }
    const request =
      action === DeliveryActions.Retry
        ? client.POST('/api/v1/integration/delivery/{id}/retry', path)
        : action === DeliveryActions.Query
          ? client.POST('/api/v1/integration/delivery/{id}/query', path)
          : action === DeliveryActions.ConfirmSucceeded
            ? client.POST('/api/v1/integration/delivery/{id}/confirm-succeeded', path)
            : action === DeliveryActions.ConfirmNotExecuted
              ? client.POST('/api/v1/integration/delivery/{id}/confirm-not-executed', path)
              : client.POST('/api/v1/integration/delivery/{id}/cancel', path)
    return request.then((r) => unwrap<DeliveryDetail>(r))
  },
}
