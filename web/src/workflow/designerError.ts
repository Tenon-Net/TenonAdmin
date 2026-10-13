import { ApiError } from '@/api'
import { findNode } from '@/workflow/model'
import type { WfNode } from '@/workflow/schema'

/** 发布/保存失败只带内部 nodeId 时,用设计器里的节点名称补上,避免把 id 弹给用户。 */
export function withDesignerNodeName(err: unknown, root: WfNode | null | undefined): unknown {
  if (!(err instanceof ApiError) || !root) return err
  const existing = typeof err.args?.nodeName === 'string' ? err.args.nodeName.trim() : ''
  if (existing) return err
  const nodeId = err.args?.nodeId == null ? '' : String(err.args.nodeId).trim()
  if (!nodeId) return err
  const nodeName = findNode(root, nodeId)?.name?.trim()
  if (!nodeName) return err
  return new ApiError(err.code, err.msgKey, { ...err.args, nodeName }, err.message)
}
