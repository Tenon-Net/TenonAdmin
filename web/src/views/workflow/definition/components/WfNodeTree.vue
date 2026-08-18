<script setup lang="ts">
// 钉钉树串行链:递归 next;节点间插入审批/抄送。
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import WfNodeCard from './WfNodeCard.vue'
import WfAddNode from './WfAddNode.vue'
import { createNode, insertAfter, removeNode } from '@/workflow/model'
import type { WfModel, WfNode, WfNodeType } from '@/workflow/schema'
import '../../wf-identity.css'

const props = defineProps<{
  model: WfModel
  selectedId?: string | null
  errorIds?: Set<string> | string[]
}>()
const emit = defineEmits<{
  'update:model': [WfModel]
  select: [node: WfNode]
}>()

const { t } = useI18n()

const errorSet = computed(() => {
  if (!props.errorIds) return new Set<string>()
  return props.errorIds instanceof Set ? props.errorIds : new Set(props.errorIds)
})

const chain = computed(() => {
  const list: WfNode[] = []
  let cur: WfNode | null | undefined = props.model.root
  while (cur) {
    list.push(cur)
    cur = cur.next ?? null
  }
  return list
})

function bump(nextRoot: WfNode) {
  emit('update:model', { ...props.model, root: nextRoot })
}

function onAdd(afterId: string, type: Extract<WfNodeType, 'approval' | 'cc'>) {
  const root = structuredClone(props.model.root)
  const node = createNode(type)
  if (!insertAfter(root, afterId, node)) return
  bump(root)
  emit('select', node)
}

function onRemove(id: string) {
  const root = structuredClone(props.model.root)
  if (!removeNode(root, id)) return
  bump(root)
}
</script>

<template>
  <div class="wf-tree">
    <template v-for="node in chain" :key="node.id">
      <WfNodeCard
        :node="node"
        :active="selectedId === node.id"
        :error="errorSet.has(node.id)"
        @select="$emit('select', node)"
        @remove="onRemove(node.id)"
      />
      <WfAddNode @add="(type) => onAdd(node.id, type)" />
    </template>
    <div class="wf-end">
      <span class="wf-end-dot" />
      <span class="wf-end-text">{{ t('workflow.designer.end') }}</span>
    </div>
  </div>
</template>

<style scoped>
.wf-tree {
  display: inline-flex;
  flex-direction: column;
  align-items: center;
  padding: 28px 48px 64px;
  min-width: min-content;
}
.wf-end {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  margin-top: -8px;
}
.wf-end-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--wf-end);
  box-shadow: 0 0 0 4px color-mix(in srgb, var(--wf-end) 16%, transparent);
}
.wf-end-text {
  font-size: var(--font-size-xs);
  color: var(--color-text-tertiary);
  letter-spacing: 0.04em;
}
</style>
