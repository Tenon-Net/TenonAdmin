<script setup lang="ts">
import { computed, inject, provide, ref, type InjectionKey } from 'vue'
import { NCollapse, NCollapseItem, NDynamicTags, NInput, NInputNumber, NSelect } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import type { WfConditionExpr, WfConditionLogic, WfConditionOp } from '@/workflow/schema'
import {
  WF_CONDITION_OPERATOR_META,
  appendConditionChild,
  classifyConditionOp,
  createConditionGroup,
  createConditionLeaf,
  isWfConditionOp,
  removeConditionChild,
  replaceConditionChild,
  setConditionOp,
} from '@/workflow/configuration'

const props = defineProps<{
  modelValue: WfConditionExpr
}>()
const emit = defineEmits<{
  'update:modelValue': [WfConditionExpr]
}>()

const { t } = useI18n()

const conditionDepthKey: InjectionKey<number> = Symbol.for('wf-condition-depth')
const depth = inject(conditionDepthKey, 0)
provide(conditionDepthKey, depth + 1)
const expandedChild = ref<string | number | null>(depth === 0 ? 'child-0' : null)

const isGroup = computed(() => props.modelValue.children != null)
const currentOp = computed<WfConditionOp>(() => props.modelValue.op ?? 'eq')
const valueKind = computed(() => classifyConditionOp(currentOp.value))
const logicOptions = computed(() => (['and', 'or'] as WfConditionLogic[]).map((value) => ({
  label: t(`workflow.condition.logic.${value}`),
  value,
})))
const opOptions = computed(() => WF_CONDITION_OPERATOR_META.map(({ op }) => ({
  label: t(`workflow.condition.op.${op}`),
  value: op,
})))

function childSummary(child: WfConditionExpr) {
  if (child.children != null) return t(`workflow.condition.logic.${child.logic ?? 'and'}`)
  return child.field || t('workflow.condition.addCondition')
}

function updateLogic(value: string | number | null) {
  if (value !== 'and' && value !== 'or') return
  emit('update:modelValue', { ...props.modelValue, logic: value })
}

function addLeaf() {
  const result = appendConditionChild(props.modelValue, createConditionLeaf())
  if (result) emit('update:modelValue', result)
}

function addGroup() {
  const result = appendConditionChild(props.modelValue, createConditionGroup())
  if (result) emit('update:modelValue', result)
}

function updateChild(index: number, child: WfConditionExpr) {
  const result = replaceConditionChild(props.modelValue, index, child)
  if (result) emit('update:modelValue', result)
}

function removeChild(index: number) {
  const result = removeConditionChild(props.modelValue, index)
  if (result) emit('update:modelValue', result)
}

function updateField(field: string) {
  emit('update:modelValue', { ...props.modelValue, field })
}

function updateOp(value: string | number | null) {
  if (!isWfConditionOp(value)) return
  emit('update:modelValue', setConditionOp(props.modelValue, value))
}

function updateValue(value: unknown) {
  emit('update:modelValue', { ...props.modelValue, value })
}
</script>

<template>
  <div class="wf-condition" :class="{ 'is-group': isGroup }">
    <template v-if="isGroup">
      <div class="wf-condition-toolbar">
        <n-select
          class="wf-logic-select"
          size="small"
          :value="modelValue.logic ?? 'and'"
          :options="logicOptions"
          :aria-label="t('workflow.condition.logicLabel')"
          @update:value="updateLogic"
        />
        <div class="wf-condition-actions">
          <button
            type="button"
            class="wf-condition-action"
            :title="t('workflow.condition.addCondition')"
            :aria-label="t('workflow.condition.addCondition')"
            @click="addLeaf"
          >
            {{ t('workflow.condition.addCondition') }}
          </button>
          <button
            type="button"
            class="wf-condition-action"
            :title="t('workflow.condition.addGroup')"
            :aria-label="t('workflow.condition.addGroup')"
            @click="addGroup"
          >
            {{ t('workflow.condition.addGroup') }}
          </button>
        </div>
      </div>

      <n-collapse
        v-if="modelValue.children?.length"
        v-model:expanded-names="expandedChild"
        accordion
        display-directive="if"
        class="wf-condition-children"
        :data-depth="depth"
      >
        <n-collapse-item
          v-for="(child, index) in modelValue.children"
          :key="index"
          :name="`child-${index}`"
        >
          <template #header>
            <span class="wf-condition-child-summary">{{ childSummary(child) }}</span>
          </template>
          <div class="wf-condition-child">
          <WfConditionEditor
            class="wf-condition-child-editor"
            :model-value="child"
            @update:model-value="updateChild(index, $event)"
          />
          <button
            type="button"
            class="wf-condition-delete"
            :title="t('common.delete')"
            :aria-label="t('common.delete')"
            @click="removeChild(index)"
          >
            ×
          </button>
          </div>
        </n-collapse-item>
      </n-collapse>
      <div v-else class="wf-condition-empty">{{ t('workflow.condition.emptyGroup') }}</div>
    </template>

    <template v-else>
      <div class="wf-condition-leaf">
        <n-input
          size="small"
          :value="modelValue.field ?? ''"
          :placeholder="t('workflow.condition.field')"
          :aria-label="t('workflow.condition.field')"
          @update:value="updateField"
        />
        <n-select
          size="small"
          :value="currentOp"
          :options="opOptions"
          :aria-label="t('workflow.condition.operator')"
          @update:value="updateOp"
        />
        <n-input-number
          v-if="valueKind === 'number'"
          class="wf-condition-value"
          size="small"
          :value="typeof modelValue.value === 'number' ? modelValue.value : 0"
          :placeholder="t('workflow.condition.value')"
          :aria-label="t('workflow.condition.value')"
          @update:value="updateValue($event ?? 0)"
        />
        <n-dynamic-tags
          v-else-if="valueKind === 'list'"
          class="wf-condition-value"
          :value="Array.isArray(modelValue.value) ? modelValue.value.map(String) : []"
          :aria-label="t('workflow.condition.value')"
          @update:value="updateValue"
        />
        <span v-else-if="valueKind === 'none'" class="wf-condition-no-value">
          {{ t('workflow.condition.noValue') }}
        </span>
        <n-input
          v-else
          class="wf-condition-value"
          size="small"
          :value="typeof modelValue.value === 'string' ? modelValue.value : ''"
          :placeholder="t('workflow.condition.value')"
          :aria-label="t('workflow.condition.value')"
          @update:value="updateValue"
        />
      </div>
    </template>
  </div>
</template>

<style scoped>
.wf-condition {
  min-width: 0;
}
.wf-condition.is-group {
  padding: var(--space-8);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-bg-body);
}
.wf-condition-toolbar,
.wf-condition-actions,
.wf-condition-child {
  display: flex;
  align-items: center;
  gap: var(--space-8);
}
.wf-condition-toolbar {
  justify-content: space-between;
}
.wf-logic-select {
  width: 104px;
}
.wf-condition-action,
.wf-condition-delete {
  border: 0;
  background: transparent;
  cursor: pointer;
  font: inherit;
}
.wf-condition-action {
  padding: var(--space-4);
  color: var(--color-primary);
  font-size: var(--font-size-xs);
}
.wf-condition-delete {
  flex: 0 0 auto;
  padding: var(--space-4) var(--space-8);
  color: var(--color-danger);
  font-size: var(--font-size-md);
}
.wf-condition-action:focus-visible,
.wf-condition-delete:focus-visible {
  outline: 2px solid var(--color-primary);
  outline-offset: 2px;
}
.wf-condition-children {
  display: flex;
  flex-direction: column;
  gap: var(--space-8);
  margin-top: var(--space-8);
  padding-left: var(--space-8);
  border-left: 1px solid var(--color-border-strong);
}
.wf-condition-child {
  align-items: flex-start;
}
.wf-condition-child-editor {
  flex: 1;
}
.wf-condition-child-summary {
  min-width: 0;
  overflow: hidden;
  color: var(--color-text-secondary);
  font-size: var(--font-size-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}
.wf-condition-empty,
.wf-condition-no-value {
  color: var(--color-text-tertiary);
  font-size: var(--font-size-xs);
}
.wf-condition-empty {
  margin-top: var(--space-8);
}
.wf-condition-leaf {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 104px;
  gap: var(--space-8);
}
.wf-condition-value,
.wf-condition-no-value {
  grid-column: 1 / -1;
}
.wf-condition-value {
  width: 100%;
}
</style>
