/** 工作流 API：路径与协议 DTO 均来自真实后端生成的 schema.d.ts。 */
import { client } from './client'
import { pageParams, toPage, unwrap } from './index'
import type {
  WfDefinitionDetail,
  WfDefinitionInput,
  WfDefinitionRow,
  WfDoneItem,
  WfEngineResult,
  WfInstanceDetail,
  WfInstanceListItem,
  WfStartableDefinition,
  WfStartableDefinitionDetail,
  WfStartInput,
  WfTaskActionInput,
  WfTodoItem,
  WfCcItem,
} from '@/types/workflow'

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

  get: (id: number) =>
    client
      .GET('/api/v1/workflow/definition/{id}', { params: { path: { id } } })
      .then((r) => unwrap<WfDefinitionDetail>(r)),

  add: (body: WfDefinitionInput) =>
    client
      .POST('/api/v1/workflow/definition/add', { body })
      .then((r) => unwrap<number | string>(r)),

  update: (body: WfDefinitionInput) =>
    client
      .POST('/api/v1/workflow/definition/update', { body })
      .then((r) => unwrap<boolean>(r)),

  publish: (id: number) =>
    client
      .POST('/api/v1/workflow/definition/publish', { body: { id } })
      .then((r) => unwrap<number | string>(r)),

  disable: (id: number) =>
    client
      .POST('/api/v1/workflow/definition/disable', { body: { id } })
      .then((r) => unwrap<boolean>(r)),

  remove: (id: number) =>
    client
      .DELETE('/api/v1/workflow/definition/{id}', { params: { path: { id } } })
      .then((r) => unwrap<boolean>(r)),
}

export const wfInstanceApi = {
  startable: () =>
    client
      .GET('/api/v1/workflow/instance/startable', {})
      .then((r) => unwrap<WfStartableDefinition[]>(r)),

  startableDetail: (id: number) =>
    client
      .GET('/api/v1/workflow/instance/startable/{id}', { params: { path: { id } } })
      .then((r) => unwrap<WfStartableDefinitionDetail>(r)),

  start: (body: WfStartInput) =>
    client
      .POST('/api/v1/workflow/instance/start', { body })
      .then((r) => unwrap<WfEngineResult>(r)),

  get: (id: number) =>
    client
      .GET('/api/v1/workflow/instance/{id}', { params: { path: { id } } })
      .then((r) => unwrap<WfInstanceDetail>(r)),

  page: (params: {
    page: number
    pageSize: number
    status?: number
    definitionId?: number
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
    definitionId?: number
    businessKey?: string
    starterUserId?: number
    actorUserId?: number
    ccUserId?: number
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

  cancel: (body: { instanceId: number; comment?: string | null; requestId?: string | null }) =>
    client
      .POST('/api/v1/workflow/instance/cancel', { body })
      .then((r) => unwrap<WfEngineResult>(r)),

  resubmit: (body: { instanceId: number; variablesJson?: string | null; requestId?: string | null }) =>
    client
      .POST('/api/v1/workflow/instance/resubmit', { body })
      .then((r) => unwrap<WfEngineResult>(r)),
}

export const wfTaskApi = {
  todo: (params: { page: number; pageSize: number; definitionId?: number }) =>
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

  done: (params: { page: number; pageSize: number; definitionId?: number }) =>
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

  urge: (body: WfTaskActionInput) =>
    client
      .POST('/api/v1/workflow/task/urge', { body })
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
