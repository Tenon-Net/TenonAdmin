// 整棵工作流的唯一 mutation coordinator:clone → model helper → 一次性回调。对应 Vue 侧 WfNodeTree.vue。
import { useMemo } from 'react'
import {
  addBranchArm,
  addParallelArm,
  cloneNode,
  createNode,
  findNode,
  insertAfter,
  insertIntoBranchArm,
  insertIntoParallelArm,
  removeBranchArm,
  removeParallelArm,
  removeNode,
} from '@/workflow/model'
import type { WfInsertableNodeType, WfModel, WfNode } from '@/workflow/schema'
import { WfNodeChain } from './WfNodeChain'
import './wf-designer.css'
import '../../wf-identity.css'

export interface WfNodeTreeProps {
  model: WfModel
  selectedId?: string | null
  errorIds?: Set<string> | string[]
  readonly?: boolean
  visitedIds?: Set<string> | string[]
  currentIds?: Set<string> | string[]
  onModelChange: (model: WfModel) => void
  onSelect: (node: WfNode) => void
}

function toSet(ids: Set<string> | string[] | undefined): Set<string> {
  if (!ids) return new Set<string>()
  return ids instanceof Set ? ids : new Set(ids)
}

export function WfNodeTree({
  model, selectedId, errorIds, readonly, visitedIds, currentIds, onModelChange, onSelect,
}: WfNodeTreeProps) {
  const errorSet = useMemo(() => toSet(errorIds), [errorIds])
  const visitedSet = useMemo(() => toSet(visitedIds), [visitedIds])
  const currentSet = useMemo(() => toSet(currentIds), [currentIds])

  const bump = (root: WfNode) => onModelChange({ ...model, root })

  const handleSelect = (nodeId: string) => {
    if (readonly) return
    const node = findNode(model.root, nodeId)
    if (node) onSelect(node)
  }

  const handleAddAfter = (afterId: string, type: WfInsertableNodeType) => {
    if (readonly) return
    const root = cloneNode(model.root)
    const node = createNode(type)
    if (!insertAfter(root, afterId, node)) return
    bump(root)
    // 插入后立刻选中新节点:用户点加号就是为了配它。
    onSelect(node)
  }

  const handleAddAtArmHead = (ownerId: string, armId: string, type: WfInsertableNodeType) => {
    if (readonly) return
    const root = cloneNode(model.root)
    const node = createNode(type)
    if (!insertIntoBranchArm(root, ownerId, armId, node)
      && !insertIntoParallelArm(root, ownerId, armId, node)) return
    bump(root)
    onSelect(node)
  }

  const handleRemoveNode = (nodeId: string) => {
    if (readonly) return
    const root = cloneNode(model.root)
    if (!removeNode(root, nodeId)) return
    bump(root)
  }

  const handleAddArm = (ownerId: string) => {
    if (readonly) return
    const root = cloneNode(model.root)
    const owner = findNode(root, ownerId)
    if (!owner || (!addBranchArm(owner) && !addParallelArm(owner))) return
    bump(root)
  }

  const handleRemoveArm = (ownerId: string, armId: string) => {
    if (readonly) return
    const root = cloneNode(model.root)
    const owner = findNode(root, ownerId)
    if (!owner || (!removeBranchArm(owner, armId) && !removeParallelArm(owner, armId))) return
    bump(root)
  }

  const handleRenameArm = (ownerId: string, armId: string, name: string) => {
    if (readonly) return
    const root = cloneNode(model.root)
    const owner = findNode(root, ownerId)
    const arms = owner?.type === 'branch'
      ? owner.conditions
      : owner?.type === 'parallel' ? owner.parallelArms : undefined
    const arm = arms?.find((item) => item.id === armId)
    if (!arm) return
    arm.name = name
    bump(root)
  }

  return (
    <div className="wf-tree">
      <WfNodeChain
        root={model.root}
        selectedId={selectedId}
        errorSet={errorSet}
        readonly={readonly}
        visitedSet={visitedSet}
        currentSet={currentSet}
        // 顶层链恒可插并行(臂内由 WfNodeChain 自己降级);漏传即等于菜单缺项,Vue 侧踩过。
        allowParallel
        terminal
        onSelect={handleSelect}
        onAddAfter={handleAddAfter}
        onAddAtArmHead={handleAddAtArmHead}
        onRemoveNode={handleRemoveNode}
        onAddArm={handleAddArm}
        onRemoveArm={handleRemoveArm}
        onRenameArm={handleRenameArm}
      />
    </div>
  )
}
