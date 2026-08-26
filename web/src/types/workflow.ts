/** 工作流协议 DTO：只从真实后端生成的 OpenAPI 契约取型。 */
import type { components } from '@/api/schema'

type Schemas = components['schemas']

export type WfDefinitionStatus = Schemas['WfDefinitionStatus']
export type WfInstanceStatus = Schemas['WfInstanceStatus']
export type WfTaskAction = Schemas['WfTaskAction']
export type WfDefinitionRow = Schemas['WfDefinition']
export type WfDefinitionDetail = Schemas['WfDefinitionDetailOutput']
export type WfDefinitionInput = Schemas['WfDefinitionInput']
export type WfStartableDefinition = Schemas['WfStartableDefinitionOutput']
export type WfStartableDefinitionDetail = Schemas['WfStartableDefinitionDetailOutput']
export type WfStartInput = Schemas['WfStartInput']
export type WfEngineResult = Schemas['WfEngineResult']
export type WfTodoItem = Schemas['WfTodoItemOutput']
export type WfDoneItem = Schemas['WfDoneItemOutput']
export type WfInstanceListItem = Schemas['WfInstanceListItemOutput']
export type WfHisTask = Schemas['WfHisTaskOutput']
export type WfInstanceDetail = Schemas['WfInstanceDetailOutput']
export type WfTaskActionInput = Schemas['WfTaskActionInput']
export type WfCcItem = Schemas['WfCcItemOutput']
