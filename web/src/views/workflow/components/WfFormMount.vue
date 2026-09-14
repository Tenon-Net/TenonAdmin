<script setup lang="ts">
/**
 * 业务表单入口:优先渲染定义级 formSchema,否则按 formComponent(views 相对路径)动态加载消费者组件。
 * 路径约定与菜单 component 一致,如 `biz/leave/form` → `/src/views/biz/leave/form.vue`。
 * 组件不存在时显示占位提示,不抛错打断审批主流程;非法双配置保留消费者挂载点。
 */
import { computed, defineAsyncComponent, type Component } from 'vue'
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { WfInstanceStatus } from '@/types/workflow'
import type { WfFormFieldPerm, WfFormSchema } from '@/workflow/schema'
import { parseWfFormValuesResult, serializeWfFormValues, type WfFormValues } from '@/workflow/formRuntime'
import WfBuiltinForm from './WfBuiltinForm.vue'

const props = defineProps<{
  formComponent?: string | null
  formSchema?: WfFormSchema | null
  mode: 'start' | 'approve' | 'view'
  permissions?: WfFormFieldPerm[] | null
  definitionId?: number
  instanceId?: number
  businessKey?: string | null
  variablesJson?: string | null
  status?: WfInstanceStatus
}>()
const emit = defineEmits<{ 'variables-change': [value: string | null] }>()

const { t } = useI18n()
const builtinRef = ref<{ validate: () => boolean } | null>(null)
const initialParse = parseWfFormValuesResult(props.variablesJson)
const builtinValues = ref<WfFormValues>(initialParse.values)
const parseError = ref(initialParse.error)

watch(() => props.variablesJson, (json) => {
  const result = parseWfFormValuesResult(json)
  builtinValues.value = result.values
  parseError.value = result.error
})

const viewModules = import.meta.glob('/src/views/**/*.vue') as Record<
  string,
  () => Promise<{ default: Component }>
>

function normalizeViewPath(raw: string): string {
  let p = raw.trim().replace(/\\/g, '/')
  if (p.startsWith('views/')) p = p.slice('views/'.length)
  if (p.startsWith('/')) p = p.slice(1)
  if (p.endsWith('.vue')) p = p.slice(0, -4)
  return p
}

const resolved = computed(() => {
  const raw = props.formComponent?.trim()
  if (!raw) return null
  const key = `/src/views/${normalizeViewPath(raw)}.vue`
  const loader = viewModules[key]
  if (!loader) return { missing: true as const, key }
  return {
    missing: false as const,
    key,
    component: defineAsyncComponent(loader),
  }
})

const builtinSchema = computed(() => {
  if (props.formComponent?.trim()) return null
  return props.formSchema?.fields?.length ? props.formSchema : null
})

function onBuiltinUpdate(value: WfFormValues) {
  builtinValues.value = value
  emit('variables-change', serializeWfFormValues(value))
}

function validate(): boolean {
  if (parseError.value) return false
  return builtinRef.value?.validate() ?? true
}

defineExpose({ validate })
</script>

<template>
  <div v-if="builtinSchema && parseError" class="wf-form-error">
    {{ t('workflow.form.runtime.invalidVariables') }}
  </div>
  <div v-else-if="builtinSchema" class="wf-form-mount">
    <WfBuiltinForm
      ref="builtinRef"
      :schema="builtinSchema"
      :model-value="builtinValues"
      :mode="mode"
      :permissions="permissions"
      @update:model-value="onBuiltinUpdate"
    />
  </div>
  <div v-else-if="formComponent" class="wf-form-mount">
    <div v-if="resolved?.missing" class="wf-form-missing">
      {{ t('workflow.form.missing', { path: formComponent }) }}
    </div>
    <component
      v-else-if="resolved && !resolved.missing"
      :is="resolved.component"
      :mode="mode"
      :definition-id="definitionId"
      :instance-id="instanceId"
      :business-key="businessKey"
      :variables-json="variablesJson"
      :status="status"
    />
  </div>
</template>

<style scoped>
.wf-form-mount {
  margin-block: 12px;
}
.wf-form-missing {
  padding: 12px 16px;
  border: 1px dashed var(--color-border);
  border-radius: var(--radius-md, 8px);
  color: var(--color-text-secondary);
  font-size: var(--font-size-sm, 13px);
}
.wf-form-error {
  padding: 12px 16px;
  border: 1px solid var(--color-danger);
  border-radius: var(--radius-md, 8px);
  color: var(--color-danger);
  font-size: var(--font-size-sm, 13px);
}
</style>
