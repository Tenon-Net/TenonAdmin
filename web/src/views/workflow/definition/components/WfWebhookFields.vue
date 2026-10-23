<script setup lang="ts">
import { computed } from 'vue'
import { NButton, NCollapse, NCollapseItem, NFormItem, NInput, NInputNumber, NSelect } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import { WF_WEBHOOK_METHODS } from '@/workflow/configuration'
import type { WfWebhookFailureAction } from '@/workflow/schema'
import type { WfWebhookFieldsState } from './drawerForm'

const model = defineModel<WfWebhookFieldsState>({ required: true })
const { t } = useI18n()

function update<K extends keyof WfWebhookFieldsState>(key: K, value: WfWebhookFieldsState[K]) {
  model.value = { ...model.value, [key]: value }
}

function updateHeader(index: number, key: 'key' | 'value', value: string) {
  update('headers', model.value.headers.map((header, headerIndex) =>
    headerIndex === index ? { ...header, [key]: value } : header,
  ))
}

function removeHeader(index: number) {
  update('headers', model.value.headers.filter((_, headerIndex) => headerIndex !== index))
}

function addHeader() {
  update('headers', [...model.value.headers, { key: '', value: '' }])
}

const methodOptions = WF_WEBHOOK_METHODS.map((value) => ({ label: value, value }))
const failureOptions = computed(() => (['fail', 'manual'] as WfWebhookFailureAction[]).map(
  (value) => ({ label: t(`workflow.webhook.failure.${value}`), value }),
))
</script>

<template>
  <n-form-item :label="t('workflow.webhook.url')" path="webhook.url">
    <n-input :value="model.url" :placeholder="t('workflow.webhook.urlPlaceholder')" @update:value="update('url', $event)" />
  </n-form-item>
  <n-form-item :label="t('workflow.webhook.method')">
    <n-select :value="model.method" :options="methodOptions" @update:value="update('method', $event)" />
  </n-form-item>
  <n-form-item :label="t('workflow.webhook.timeoutSeconds')" path="webhook.timeoutSeconds">
    <n-input-number :value="model.timeoutSeconds" :min="1" :max="120" :precision="0" class="w-full" @update:value="update('timeoutSeconds', $event)" />
  </n-form-item>
  <n-form-item :label="t('workflow.webhook.onFailure')">
    <n-select :value="model.onFailure" :options="failureOptions" @update:value="update('onFailure', $event)" />
  </n-form-item>
  <n-collapse class="wf-advanced" display-directive="show">
    <n-collapse-item name="advanced">
      <template #header>
        <span class="wf-advanced-title">{{ t('workflow.designer.advanced') }}</span>
      </template>
      <n-form-item :label="t('workflow.webhook.headers')">
        <div class="wf-webhook-headers">
          <div v-for="(header, index) in model.headers" :key="index" class="wf-webhook-header">
            <n-input :value="header.key" :placeholder="t('workflow.webhook.headerName')" @update:value="updateHeader(index, 'key', $event)" />
            <n-input :value="header.value" :placeholder="t('workflow.webhook.headerValue')" @update:value="updateHeader(index, 'value', $event)" />
            <n-button quaternary circle :aria-label="t('workflow.webhook.removeHeader')" :title="t('workflow.webhook.removeHeader')" @click="removeHeader(index)">
              <template #icon><AppIcon icon="ph:x" :size="14" /></template>
            </n-button>
          </div>
          <n-button dashed block @click="addHeader">
            <template #icon><AppIcon icon="ph:plus" :size="14" /></template>
            {{ t('workflow.webhook.addHeader') }}
          </n-button>
        </div>
      </n-form-item>
      <n-form-item :label="t('workflow.webhook.maxAttempts')" path="webhook.maxAttempts">
        <n-input-number :value="model.maxAttempts" :min="1" :max="100" :precision="0" class="w-full" @update:value="update('maxAttempts', $event)" />
      </n-form-item>
    </n-collapse-item>
  </n-collapse>
</template>

<style scoped>
.w-full { width: 100%; }
.wf-webhook-headers { display: flex; flex-direction: column; gap: var(--space-8); width: 100%; }
.wf-webhook-header { display: grid; grid-template-columns: 1fr 1fr auto; gap: var(--space-8); align-items: center; }
</style>
