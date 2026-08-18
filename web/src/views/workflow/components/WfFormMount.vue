<script setup lang="ts">
/**
 * 业务表单挂载点:按定义里的 formComponent(views 相对路径)动态加载消费者组件。
 * 路径约定与菜单 component 一致,如 `biz/leave/form` → `/src/views/biz/leave/form.vue`。
 * 组件不存在时显示占位提示,不抛错打断审批主流程。
 */
import { computed, defineAsyncComponent, type Component } from 'vue'
import { useI18n } from 'vue-i18n'
import type { WfInstanceStatus } from '@/types/workflow'

const props = defineProps<{
  formComponent?: string | null
  mode: 'start' | 'view'
  definitionId?: number
  instanceId?: number
  businessKey?: string | null
  variablesJson?: string | null
  status?: WfInstanceStatus
}>()

const { t } = useI18n()

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
</script>

<template>
  <div v-if="formComponent" class="wf-form-mount">
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
</style>
