<script setup lang="ts">
// 整棵工作流的唯一 mutation coordinator:clone → model helper → 一次性 emit。
import { computed } from 'vue'
import {
  addBranchArm,
  cloneNode,
  createNode,
  findNode,
  insertAfter,
  insertIntoBranchArm,
  removeBranchArm,
  removeNode,
} from '@/workflow/model'
import type { WfModel, WfNode, WfNodeType } from '@/workflow/schema'
import WfNodeChain from './WfNodeChain.vue'
import '../../wf-identity.css'

type InsertableNodeType = Extract<WfNodeType, 'approval' | 'cc' | 'branch'>

const props = defineProps<{
  model: WfModel
  selectedId?: string | null
  errorIds?: Set<string> | string[]
}>()
const emit = defineEmits<{
  'update:model': [WfModel]
  select: [node: WfNode]
}>()

const errorSet = computed(() => {
  if (!props.errorIds) return new Set<string>()
  return props.errorIds instanceof Set ? props.errorIds : new Set(props.errorIds)
})

function bump(root: WfNode) {
  emit('update:model', { ...props.model, root })
}

function onSelect(nodeId: string) {
  const node = findNode(props.model.root, nodeId)
  if (node) emit('select', node)
}

function onAddAfter(afterId: string, type: InsertableNodeType) {
  const root = cloneNode(props.model.root)
  const node = createNode(type)
  if (!insertAfter(root, afterId, node)) return
  bump(root)
  emit('select', node)
}

function onAddAtArmHead(branchId: string, armId: string, type: InsertableNodeType) {
  const root = cloneNode(props.model.root)
  const node = createNode(type)
  if (!insertIntoBranchArm(root, branchId, armId, node)) return
  bump(root)
  emit('select', node)
}

function onRemoveNode(nodeId: string) {
  const root = cloneNode(props.model.root)
  if (!removeNode(root, nodeId)) return
  bump(root)
}

function onAddArm(branchId: string) {
  const root = cloneNode(props.model.root)
  const branch = findNode(root, branchId)
  if (!branch || !addBranchArm(branch)) return
  bump(root)
}

function onRemoveArm(branchId: string, armId: string) {
  const root = cloneNode(props.model.root)
  const branch = findNode(root, branchId)
  if (!branch || !removeBranchArm(branch, armId)) return
  bump(root)
}

function onRenameArm(branchId: string, armId: string, name: string) {
  const root = cloneNode(props.model.root)
  const branch = findNode(root, branchId)
  if (branch?.type !== 'branch') return
  const arm = branch.conditions?.find((item) => item.id === armId)
  if (!arm) return
  arm.name = name
  bump(root)
}
</script>

<template>
  <div class="wf-tree">
    <WfNodeChain
      :root="model.root"
      :selected-id="selectedId"
      :error-set="errorSet"
      terminal
      @select="onSelect"
      @add-after="onAddAfter"
      @add-at-arm-head="onAddAtArmHead"
      @remove-node="onRemoveNode"
      @add-arm="onAddArm"
      @remove-arm="onRemoveArm"
      @rename-arm="onRenameArm"
    />
  </div>
</template>

<style scoped>
.wf-tree {
  display: inline-flex;
  flex-direction: column;
  align-items: center;
  min-width: min-content;
  padding: 28px 48px 64px;
}
</style>
