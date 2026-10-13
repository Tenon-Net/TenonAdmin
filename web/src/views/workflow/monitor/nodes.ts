import { projectWfRuntimeModel } from '@/workflow/formSchema'
import { findNode } from '@/workflow/model'

export interface MonitorNodeOption {
  id: string
  name: string
}

/** 强制转办/跳过的节点选项:当前待办节点,再加上没有待办的阻断节点。 */
export function monitorNodeOptions(detail: {
  currentTasks?: Array<{ nodeId?: string | null; nodeName?: string | null }> | null
  blockedNodeIds?: readonly string[] | null
  model?: unknown
}): MonitorNodeOption[] {
  const seen = new Set<string>()
  const options: MonitorNodeOption[] = []
  for (const task of detail.currentTasks ?? []) {
    const id = task.nodeId?.trim()
    if (!id || seen.has(id)) continue
    seen.add(id)
    options.push({ id, name: task.nodeName?.trim() || id })
  }
  const model = projectWfRuntimeModel(detail.model)
  for (const raw of detail.blockedNodeIds ?? []) {
    const id = raw?.trim()
    if (!id || seen.has(id)) continue
    seen.add(id)
    const name = model ? findNode(model.root, id)?.name?.trim() : ''
    options.push({ id, name: name || id })
  }
  return options
}

/** 只有一个进行中或阻断节点时预选它;并行多节点留给管理员指定。 */
export function defaultMonitorNodeId(options: readonly MonitorNodeOption[]): string | null {
  return options.length === 1 ? options[0]!.id : null
}
