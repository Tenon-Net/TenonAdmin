<script setup lang="ts">
// 单条局部链的递归展示层:只上抛稳定 Id/节点类型/名称,不修改传入模型。
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import type { WfNode, WfNodeType } from '@/workflow/schema'
import WfAddNode from './WfAddNode.vue'
import WfNodeCard from './WfNodeCard.vue'
import '../../wf-identity.css'

type InsertableNodeType = Extract<WfNodeType, 'approval' | 'cc' | 'branch'>

const props = defineProps<{
  root: WfNode | null | undefined
  selectedId?: string | null
  errorSet: Set<string>
  terminal?: boolean
  readonly?: boolean
  visitedSet?: Set<string>
  currentSet?: Set<string>
}>()
const emit = defineEmits<{
  select: [nodeId: string]
  'add-after': [afterId: string, type: InsertableNodeType]
  'add-at-arm-head': [branchId: string, armId: string, type: InsertableNodeType]
  'remove-node': [nodeId: string]
  'add-arm': [branchId: string]
  'remove-arm': [branchId: string, armId: string]
  'rename-arm': [branchId: string, armId: string, name: string]
}>()

const { t } = useI18n()

/** 本组件只展开当前局部 next 链;分支臂由模板递归,避免把臂节点重复渲染到主链。 */
const chain = computed(() => {
  const nodes: WfNode[] = []
  let current = props.root ?? null
  while (current) {
    nodes.push(current)
    current = current.next ?? null
  }
  return nodes
})

function renameArm(branchId: string, armId: string, event: Event) {
  emit('rename-arm', branchId, armId, (event.target as HTMLInputElement).value)
}

function forwardSelect(nodeId: string) {
  emit('select', nodeId)
}

function forwardAddAfter(afterId: string, type: InsertableNodeType) {
  emit('add-after', afterId, type)
}

function forwardAddAtArmHead(branchId: string, armId: string, type: InsertableNodeType) {
  emit('add-at-arm-head', branchId, armId, type)
}

function forwardRemoveNode(nodeId: string) {
  emit('remove-node', nodeId)
}

function forwardAddArm(branchId: string) {
  emit('add-arm', branchId)
}

function forwardRemoveArm(branchId: string, armId: string) {
  emit('remove-arm', branchId, armId)
}

function forwardRenameArm(branchId: string, armId: string, name: string) {
  emit('rename-arm', branchId, armId, name)
}
</script>

<template>
  <div class="wf-chain">
    <div v-for="node in chain" :key="node.id" class="wf-chain-node">
      <WfNodeCard
        :node="node"
        :active="selectedId === node.id"
        :error="errorSet.has(node.id)"
        :readonly="readonly"
        :visited="visitedSet?.has(node.id) ?? false"
        :current="currentSet?.has(node.id) ?? false"
        @select="emit('select', node.id)"
        @remove="emit('remove-node', node.id)"
      />

      <section v-if="node.type === 'branch'" class="wf-branch" :aria-label="t('workflow.node.branch')">
        <div class="wf-branch-toolbar">
          <span>{{ t('workflow.designer.armCount', { count: node.conditions?.length ?? 0 }) }}</span>
          <button
            v-if="!readonly"
            type="button"
            class="wf-arm-action wf-arm-add"
            :aria-label="t('workflow.designer.addArm')"
            @click="emit('add-arm', node.id)"
          >
            <AppIcon icon="ph:plus-bold" :size="13" />
            <span>{{ t('workflow.designer.addArm') }}</span>
          </button>
        </div>

        <div class="wf-branch-arms">
          <article v-for="arm in node.conditions ?? []" :key="arm.id" class="wf-arm">
            <header class="wf-arm-head">
              <input
                v-if="!readonly"
                class="wf-arm-name"
                type="text"
                :value="arm.name"
                :placeholder="t('workflow.designer.armName')"
                :aria-label="t('workflow.designer.armName')"
                @change="renameArm(node.id, arm.id, $event)"
              >
              <span v-else class="wf-arm-name">{{ arm.name || t('workflow.designer.armName') }}</span>
              <span v-if="arm.isDefault" class="wf-arm-default">{{ t('common.isDefault') }}</span>
              <button
                v-else-if="!readonly"
                type="button"
                class="wf-arm-action wf-arm-remove"
                :aria-label="t('common.delete')"
                :title="t('common.delete')"
                @click="emit('remove-arm', node.id, arm.id)"
              >
                <AppIcon icon="ph:x" :size="13" />
              </button>
            </header>

            <div class="wf-arm-body">
              <WfAddNode v-if="!readonly" @add="(type) => emit('add-at-arm-head', node.id, arm.id, type)" />
              <WfNodeChain
                v-if="arm.next"
                :root="arm.next"
                :selected-id="selectedId"
                :error-set="errorSet"
                :readonly="readonly"
                :visited-set="visitedSet"
                :current-set="currentSet"
                :terminal="false"
                @select="forwardSelect"
                @add-after="forwardAddAfter"
                @add-at-arm-head="forwardAddAtArmHead"
                @remove-node="forwardRemoveNode"
                @add-arm="forwardAddArm"
                @remove-arm="forwardRemoveArm"
                @rename-arm="forwardRenameArm"
              />
              <div class="wf-arm-merge">
                <span class="wf-arm-merge-dot" />
                <span>{{ t('workflow.designer.merge') }}</span>
              </div>
            </div>
          </article>
        </div>
      </section>

      <WfAddNode v-if="!readonly" @add="(type) => emit('add-after', node.id, type)" />
    </div>

    <div v-if="terminal" class="wf-chain-terminal">
      <span class="wf-chain-terminal-dot" />
      <span>{{ t('workflow.designer.end') }}</span>
    </div>
  </div>
</template>

<style scoped>
.wf-chain,
.wf-chain-node {
  display: flex;
  flex-direction: column;
  align-items: center;
  min-width: max-content;
}

.wf-branch {
  position: relative;
  margin-top: var(--space-24);
  padding: var(--space-12) var(--space-16) var(--space-16);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  background: color-mix(in srgb, var(--color-bg-container) 92%, var(--color-primary-light));
  box-shadow: var(--shadow-1);
}
.wf-branch::before {
  content: '';
  position: absolute;
  top: calc(-1 * var(--space-24));
  left: 50%;
  width: 2px;
  height: var(--space-24);
  transform: translateX(-50%);
  background: var(--color-border-strong);
}
.wf-branch-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-16);
  min-height: var(--space-32);
  color: var(--color-text-tertiary);
  font-size: var(--font-size-xs);
}
.wf-branch-arms {
  display: flex;
  align-items: stretch;
  gap: var(--space-16);
  padding-top: var(--space-8);
}
.wf-arm {
  flex: 0 0 auto;
  width: max-content;
  min-width: calc(220px + var(--space-32));
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg-container);
}
.wf-arm-head {
  display: flex;
  align-items: center;
  gap: var(--space-8);
  min-height: var(--space-32);
  padding: var(--space-4) var(--space-8);
  border-bottom: 1px solid var(--color-border);
  background: var(--color-fill);
}
.wf-arm-name {
  flex: 1;
  min-width: 0;
  height: calc(var(--space-24) + 2px);
  padding: 0 var(--space-8);
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  outline: none;
  background: transparent;
  color: var(--color-text-primary);
  font: inherit;
  font-size: var(--font-size-sm);
}
.wf-arm-name:hover {
  border-color: var(--color-border-strong);
  background: var(--color-bg-container);
}
.wf-arm-name:focus-visible {
  border-color: var(--color-primary);
  background: var(--color-bg-container);
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--color-primary) 20%, transparent);
}
.wf-arm-default {
  flex-shrink: 0;
  padding: var(--space-4) var(--space-8);
  border-radius: 999px;
  background: var(--color-primary-light);
  color: var(--color-primary);
  font-size: var(--font-size-xs);
}
.wf-arm-action {
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--color-text-secondary);
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-4);
}
.wf-arm-action:hover {
  background: var(--color-fill-hover);
  color: var(--color-text-primary);
}
.wf-arm-action:focus-visible {
  outline: 2px solid var(--color-primary);
  outline-offset: 2px;
}
.wf-arm-add {
  min-height: calc(var(--space-24) + 2px);
  padding: 0 var(--space-8);
  color: var(--color-primary);
}
.wf-arm-remove {
  width: calc(var(--space-24) + 2px);
  height: calc(var(--space-24) + 2px);
  flex-shrink: 0;
}
.wf-arm-body {
  display: flex;
  flex-direction: column;
  align-items: center;
  min-height: calc(5 * var(--space-24) + var(--space-4));
  padding: 0 var(--space-16) var(--space-16);
}
.wf-arm-merge,
.wf-chain-terminal {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--space-4);
  color: var(--color-text-tertiary);
  font-size: var(--font-size-xs);
  letter-spacing: 0.04em;
}
.wf-arm-merge {
  position: relative;
  width: 100%;
  padding-top: var(--space-24);
}
.wf-arm-merge::before {
  content: '';
  position: absolute;
  top: 0;
  left: 50%;
  width: 2px;
  height: var(--space-24);
  transform: translateX(-50%);
  background: var(--color-border-strong);
}
.wf-arm-merge-dot,
.wf-chain-terminal-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--wf-end);
  box-shadow: 0 0 0 4px color-mix(in srgb, var(--wf-end) 16%, transparent);
}
.wf-chain-terminal {
  margin-top: calc(-1 * var(--space-8));
}

:global(html[data-theme='dark']) .wf-branch {
  background: color-mix(in srgb, var(--color-bg-container) 90%, var(--color-primary-light));
}
</style>
