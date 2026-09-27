/** 工作流 API：路径与协议 DTO 均来自真实后端生成的 schema.d.ts。 */
import { client } from './client'
import { pageParams, toPage, unwrap } from './index'
import type {
  WfDefinitionDetail,
  WfDefinitionIdInput,
  WfDefinitionInput,
  WfDefinitionRow,
  WfDoneItem,
  WfEngineResult,
  WfInstanceDetail,
  WfInstanceCancelInput,
  WfInstanceResubmitInput,
  WfHistoryItem,
  WfInstanceListItem,
  WfStartableDefinition,
  WfStartableDefinitionDetail,
  WfStartInput,
  WfTaskActionInput,
  WfTodoItem,
  WfCcItem,
  WfDelegationRule,
  WfDelegationRuleInput,
} from '@/types/workflow'
import type { WfId } from '@/workflow/id'

export const wfDefinitionApi = {
  page: (params: {
    page: number
    pageSize: number
    name?: string
    groupName?: string
    status?: number
  }) =>
    client
      .GET('/api/v1/workflow/definition/page', {
        params: {
          query: {
            ...pageParams(params),
            Name: params.name,
            GroupName: params.groupName,
            Status: params.status,
          },
        },
      })
      .then((r) => toPage<WfDefinitionRow>(r)),

  get: (id: WfId) =>
    client
      .GET('/api/v1/workflow/definition/{id}', { params: { path: { id: id as number } } })
      .then((r) => unwrap<WfDefinitionDetail>(r)),

  add: (body: WfDefinitionInput) =>
    client
      .POST('/api/v1/workflow/definition/add', { body })
      .then((r) => unwrap<number | string>(r)),

  update: (body: WfDefinitionInput) =>
    client
      .POST('/api/v1/workflow/definition/update', { body })
      .then((r) => unwrap<boolean>(r)),

  publish: (id: WfId) =>
    client
      .POST('/api/v1/workflow/definition/publish', { body: { id } satisfies WfDefinitionIdInput })
      .then((r) => unwrap<number | string>(r)),

  disable: (id: WfId) =>
    client
      .POST('/api/v1/workflow/definition/disable', { body: { id } satisfies WfDefinitionIdInput })
      .then((r) => unwrap<boolean>(r)),

  remove: (id: WfId) =>
    client
      .DELETE('/api/v1/workflow/definition/{id}', { params: { path: { id: id as number } } })
      .then((r) => unwrap<boolean>(r)),
}

export const wfInstanceApi = {
  startable: () =>
    client
      .GET('/api/v1/workflow/instance/startable', {})
      .then((r) => unwrap<WfStartableDefinition[]>(r)),

  startableDetail: (id: WfId) =>
    client
      .GET('/api/v1/workflow/instance/startable/{id}', { params: { path: { id: id as number } } })
      .then((r) => unwrap<WfStartableDefinitionDetail>(r)),

  start: (body: WfStartInput) =>
    client
      .POST('/api/v1/workflow/instance/start', { body })
      .then((r) => unwrap<WfEngineResult>(r)),

  get: (id: WfId) =>
    client
      .GET('/api/v1/workflow/instance/{id}', { params: { path: { id: id as number } } })
      .then((r) => unwrap<WfInstanceDetail>(r)),

  history: (id: WfId) =>
    client
      .GET('/api/v1/workflow/instance/history/{id}', { params: { path: { id: id as number } } })
      .then((r) => unwrap<WfHistoryItem[]>(r)),

  page: (params: {
    page: number
    pageSize: number
    status?: number
    definitionId?: WfId
    businessKey?: string
  }) =>
    client
      .GET('/api/v1/workflow/instance/page', {
        params: {
          query: {
            ...pageParams(params),
            Status: params.status,
            DefinitionId: params.definitionId,
            BusinessKey: params.businessKey,
          },
        },
      })
      .then((r) => toPage<WfInstanceListItem>(r)),

  monitor: (params: {
    page: number
    pageSize: number
    status?: number
    definitionId?: WfId
    businessKey?: string
    starterUserId?: WfId
    actorUserId?: WfId
    ccUserId?: WfId
  }) =>
    client
      .GET('/api/v1/workflow/instance/monitor', {
        params: {
          query: {
            ...pageParams(params),
            Status: params.status,
            DefinitionId: params.definitionId,
            BusinessKey: params.businessKey,
            StarterUserId: params.starterUserId,
            ActorUserId: params.actorUserId,
            CcUserId: params.ccUserId,
          },
        },
      })
      .then((r) => toPage<WfInstanceListItem>(r)),

  cancel: (body: WfInstanceCancelInput) =>
    client
      .POST('/api/v1/workflow/instance/cancel', { body })
      .then((r) => unwrap<WfEngineResult>(r)),

  resubmit: (body: WfInstanceResubmitInput) =>
    client
      .POST('/api/v1/workflow/instance/resubmit', { body })
      .then((r) => unwrap<WfEngineResult>(r)),
}

export const wfTaskApi = {
  todo: (params: { page: number; pageSize: number; definitionId?: WfId }) =>
    client
      .GET('/api/v1/workflow/task/todo', {
        params: {
          query: {
            ...pageParams(params),
            DefinitionId: params.definitionId,
          },
        },
      })
      .then((r) => toPage<WfTodoItem>(r)),

  done: (params: { page: number; pageSize: number; definitionId?: WfId }) =>
    client
      .GET('/api/v1/workflow/task/done', {
        params: {
          query: {
            ...pageParams(params),
            DefinitionId: params.definitionId,
          },
        },
      })
      .then((r) => toPage<WfDoneItem>(r)),

  approve: (body: WfTaskActionInput) =>
    client
      .POST('/api/v1/workflow/task/approve', { body })
      .then((r) => unwrap<WfEngineResult>(r)),

  reject: (body: WfTaskActionInput) =>
    client
      .POST('/api/v1/workflow/task/reject', { body })
      .then((r) => unwrap<WfEngineResult>(r)),

  transfer: (body: WfTaskActionInput) =>
    client
      .POST('/api/v1/workflow/task/transfer', { body })
      .then((r) => unwrap<WfEngineResult>(r)),

  return: (body: WfTaskActionInput) =>
    client
      .POST('/api/v1/workflow/task/return', { body })
      .then((r) => unwrap<WfEngineResult>(r)),

  delegate: (body: WfTaskActionInput) =>
    client
      .POST('/api/v1/workflow/task/delegate', { body })
      .then((r) => unwrap<WfEngineResult>(r)),

  addSign: (body: WfTaskActionInput) =>
    client
      .POST('/api/v1/workflow/task/add-sign', { body })
      .then((r) => unwrap<WfEngineResult>(r)),

  removeSign: (body: WfTaskActionInput) =>
    client
      .POST('/api/v1/workflow/task/remove-sign', { body })
      .then((r) => unwrap<WfEngineResult>(r)),

  takeBack: (body: WfTaskActionInput) =>
    client
      .POST('/api/v1/workflow/task/take-back', { body })
      .then((r) => unwrap<WfEngineResult>(r)),

  urge: (body: WfTaskActionInput) =>
    client
      .POST('/api/v1/workflow/task/urge', { body })
      .then((r) => unwrap<boolean>(r)),
}

export const wfDelegationApi = {
  page: (params: { page: number; pageSize: number; originalUserId?: WfId; enabled?: boolean }) =>
    client
      .GET('/api/v1/workflow/delegation/page', {
        params: {
          query: {
            ...pageParams(params),
            OriginalUserId: params.originalUserId,
            Enabled: params.enabled,
          },
        },
      })
      .then((r) => toPage<WfDelegationRule>(r)),

  add: (body: WfDelegationRuleInput) =>
    client.POST('/api/v1/workflow/delegation/add', { body }).then((r) => unwrap<WfDelegationRule>(r)),

  update: (id: WfId, body: WfDelegationRuleInput) =>
    client
      .PUT('/api/v1/workflow/delegation/{id}', { params: { path: { id } }, body })
      .then((r) => unwrap<WfDelegationRule>(r)),

  remove: (id: WfId, requestId: string) =>
    client
      .DELETE('/api/v1/workflow/delegation/{id}', {
        params: { path: { id }, query: { requestId } },
      })
      .then((r) => unwrap<boolean>(r)),
}

export const wfCcApi = {
  page: (params: { page: number; pageSize: number; onlyUnread?: boolean }) =>
    client
      .GET('/api/v1/workflow/cc/page', {
        params: {
          query: {
            ...pageParams(params),
            OnlyUnread: params.onlyUnread,
          },
        },
      })
      .then((r) => toPage<WfCcItem>(r)),
}
