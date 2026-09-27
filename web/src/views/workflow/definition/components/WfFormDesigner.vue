<script setup lang="ts">
import { computed } from 'vue'
import {
  NButton, NCard, NFormItem, NInput, NInputNumber, NSelect, NSpace, NSwitch,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import {
  addWfFormField,
  createWfFormField,
  getWfFormFieldProp,
  moveWfFormField,
  removeWfFormField,
  updateWfFormFieldProp,
  validateWfFormSchema,
} from '@/workflow/formSchema'
import {
  WF_FORM_FIELD_TYPES,
  type WfFormField,
  type WfFormFieldType,
  type WfFormOption,
  type WfFormSchema,
} from '@/workflow/schema'

const props = defineProps<{ modelValue: WfFormSchema | null }>()
const emit = defineEmits<{ 'update:modelValue': [WfFormSchema | null] }>()
const { t } = useI18n()

const typeOptions = computed(() => WF_FORM_FIELD_TYPES.map((value) => ({
  value,
  label: t(`workflow.form.type.${value}`),
})))
const issues = computed(() => validateWfFormSchema(props.modelValue))
const schemaErrors = computed(() => [...new Set(
  issues.value.filter((issue) => issue.fieldIndex == null).map((issue) => t(`workflow.form.error.${issue.code}`)),
)])

function updateField(index: number, update: (field: WfFormField) => WfFormField) {
  if (!props.modelValue) return
  const fields = [...props.modelValue.fields]
  const field = fields[index]
  if (!field) return
  fields[index] = update(field)
  emit('update:modelValue', { version: 1, fields })
}

function updateProp(index: number, key: string, value: unknown) {
  const field = props.modelValue?.fields[index]
  if (!field) return
  updateField(index, (current) => updateWfFormFieldProp(current, key, value))
}

function prop(field: WfFormField, key: string): unknown {
  return getWfFormFieldProp(field, key)
}

function numberProp(field: WfFormField, key: string): number | null {
  const value = prop(field, key)
  return typeof value === 'number' ? value : null
}

function stringProp(field: WfFormField, key: string): string {
  const value = prop(field, key)
  return typeof value === 'string' ? value : ''
}

function booleanProp(field: WfFormField, key: string): boolean {
  return prop(field, key) === true
}

function options(field: WfFormField): WfFormOption[] {
  const value = prop(field, 'options')
  return Array.isArray(value) ? value : []
}

function updateOption(fieldIndex: number, optionIndex: number, patch: Partial<WfFormOption>) {
  const field = props.modelValue?.fields[fieldIndex]
  if (!field) return
  const next = options(field).map((option, index) => index === optionIndex ? { ...option, ...patch } : option)
  updateProp(fieldIndex, 'options', next)
}

function addOption(fieldIndex: number) {
  const field = props.modelValue?.fields[fieldIndex]
  if (!field) return
  const current = options(field)
  let n = current.length + 1
  while (current.some((option) => option.value === `option${n}`)) n += 1
  updateProp(fieldIndex, 'options', [...current, { label: `${t('workflow.form.option')} ${n}`, value: `option${n}` }])
}

function removeOption(fieldIndex: number, optionIndex: number) {
  const field = props.modelValue?.fields[fieldIndex]
  if (field) updateProp(fieldIndex, 'options', options(field).filter((_, index) => index !== optionIndex))
}

function changeType(index: number, type: WfFormFieldType) {
  const field = props.modelValue?.fields[index]
  if (!field) return
  updateField(index, () => ({ ...createWfFormField(type, field.key), label: field.label, required: field.required, placeholder: field.placeholder }))
}

function fieldErrors(index: number): string[] {
  return [...new Set(issues.value.filter((issue) => issue.fieldIndex === index).map((issue) => t(`workflow.form.error.${issue.code}`)))]
}

function addField(type: string) {
  if (WF_FORM_FIELD_TYPES.includes(type as WfFormFieldType)) emit('update:modelValue', addWfFormField(props.modelValue, type as WfFormFieldType))
}
</script>

<template>
  <div class="wf-form-designer">
    <div class="wf-form-toolbar">
      <n-select class="wf-form-type-select" :options="typeOptions" :value="null" :disabled="(modelValue?.fields.length ?? 0) >= 50" :placeholder="t('workflow.form.addField')" @update:value="addField" />
      <span class="wf-form-count">{{ modelValue?.fields.length ?? 0 }}/50</span>
    </div>
    <div v-if="schemaErrors.length" class="wf-form-errors">{{ schemaErrors.join('；') }}</div>

    <n-card v-for="(field, index) in modelValue?.fields ?? []" :key="index" size="small" class="wf-form-field">
      <template #header>
        <span>{{ field.label || field.key || t('workflow.form.unnamedField') }}</span>
      </template>
      <template #header-extra>
        <n-space :size="4">
          <n-button quaternary circle size="small" :disabled="index === 0" :aria-label="t('workflow.form.moveUp')" @click="emit('update:modelValue', moveWfFormField(modelValue!, index, -1))"><template #icon><AppIcon icon="ph:arrow-up" /></template></n-button>
          <n-button quaternary circle size="small" :disabled="index === modelValue!.fields.length - 1" :aria-label="t('workflow.form.moveDown')" @click="emit('update:modelValue', moveWfFormField(modelValue!, index, 1))"><template #icon><AppIcon icon="ph:arrow-down" /></template></n-button>
          <n-button quaternary circle size="small" type="error" :aria-label="t('workflow.form.removeField')" @click="emit('update:modelValue', removeWfFormField(modelValue!, index))"><template #icon><AppIcon icon="ph:trash" /></template></n-button>
        </n-space>
      </template>

      <div class="wf-form-grid">
        <n-form-item :label="t('workflow.form.fieldType')"><n-select :value="field.type" :options="typeOptions" @update:value="changeType(index, $event)" /></n-form-item>
        <n-form-item :label="t('workflow.form.key')"><n-input :value="field.key" maxlength="64" @update:value="updateField(index, (current) => ({ ...current, key: $event }))" /></n-form-item>
        <n-form-item :label="t('workflow.form.label')"><n-input :value="field.label" maxlength="128" @update:value="updateField(index, (current) => ({ ...current, label: $event }))" /></n-form-item>
        <n-form-item :label="t('workflow.form.placeholder')"><n-input :value="field.placeholder ?? ''" maxlength="256" @update:value="updateField(index, (current) => ({ ...current, placeholder: $event }))" /></n-form-item>
        <n-form-item :label="t('workflow.form.required')"><n-switch :value="field.required" @update:value="updateField(index, (current) => ({ ...current, required: $event }))" /></n-form-item>
      </div>

      <div v-if="field.type === 'text' || field.type === 'textarea'" class="wf-form-grid">
        <n-form-item :label="t('workflow.form.maxLength')"><n-input-number :value="numberProp(field, 'maxLength')" :min="1" :max="field.type === 'text' ? 256 : 4000" @update:value="updateProp(index, 'maxLength', $event)" /></n-form-item>
        <n-form-item v-if="field.type === 'textarea'" :label="t('workflow.form.rows')"><n-input-number :value="numberProp(field, 'rows')" :min="2" :max="8" @update:value="updateProp(index, 'rows', $event)" /></n-form-item>
      </div>
      <div v-else-if="field.type === 'number' || field.type === 'money'" class="wf-form-grid">
        <n-form-item :label="t('workflow.form.min')"><n-input-number :value="numberProp(field, 'min')" @update:value="updateProp(index, 'min', $event)" /></n-form-item>
        <n-form-item :label="t('workflow.form.max')"><n-input-number :value="numberProp(field, 'max')" @update:value="updateProp(index, 'max', $event)" /></n-form-item>
        <n-form-item v-if="field.type === 'number'" :label="t('workflow.form.precision')"><n-input-number :value="numberProp(field, 'precision')" :min="0" :max="6" @update:value="updateProp(index, 'precision', $event)" /></n-form-item>
      </div>
      <div v-else-if="field.type === 'date' || field.type === 'datetime'" class="wf-form-grid">
        <n-form-item :label="t('workflow.form.min')"><n-input :value="stringProp(field, 'min')" :placeholder="field.type === 'date' ? 'YYYY-MM-DD' : '2026-01-01T00:00:00+08:00'" @update:value="updateProp(index, 'min', $event)" /></n-form-item>
        <n-form-item :label="t('workflow.form.max')"><n-input :value="stringProp(field, 'max')" :placeholder="field.type === 'date' ? 'YYYY-MM-DD' : '2026-12-31T23:59:59+08:00'" @update:value="updateProp(index, 'max', $event)" /></n-form-item>
      </div>
      <template v-else-if="field.type === 'select' || field.type === 'multiSelect'">
        <div v-for="(option, optionIndex) in options(field)" :key="optionIndex" class="wf-form-option">
          <n-input :value="option.label" :placeholder="t('workflow.form.optionLabel')" @update:value="updateOption(index, optionIndex, { label: $event })" />
          <n-input :value="option.value" :placeholder="t('workflow.form.optionValue')" @update:value="updateOption(index, optionIndex, { value: $event })" />
          <n-button quaternary circle :aria-label="t('workflow.form.removeOption')" @click="removeOption(index, optionIndex)"><template #icon><AppIcon icon="ph:x" /></template></n-button>
        </div>
        <n-button dashed block @click="addOption(index)">{{ t('workflow.form.addOption') }}</n-button>
        <n-form-item v-if="field.type === 'multiSelect'" :label="t('workflow.form.maxSelected')"><n-input-number :value="numberProp(field, 'maxSelected')" :min="1" :max="100" @update:value="updateProp(index, 'maxSelected', $event)" /></n-form-item>
      </template>
      <div v-else-if="field.type === 'user'" class="wf-form-grid">
        <n-form-item :label="t('workflow.form.multiple')"><n-switch :value="booleanProp(field, 'multiple')" @update:value="updateProp(index, 'multiple', $event)" /></n-form-item>
        <n-form-item v-if="booleanProp(field, 'multiple')" :label="t('workflow.form.maxSelected')"><n-input-number :value="numberProp(field, 'maxSelected')" :min="1" :max="100" @update:value="updateProp(index, 'maxSelected', $event)" /></n-form-item>
      </div>
      <div v-else-if="field.type === 'attachment'" class="wf-form-grid">
        <n-form-item :label="t('workflow.form.multiple')"><n-switch :value="booleanProp(field, 'multiple')" @update:value="updateProp(index, 'multiple', $event)" /></n-form-item>
        <n-form-item :label="t('workflow.form.maxCount')"><n-input-number :value="numberProp(field, 'maxCount')" :min="1" :max="20" :disabled="!booleanProp(field, 'multiple')" @update:value="updateProp(index, 'maxCount', $event)" /></n-form-item>
        <n-form-item :label="t('workflow.form.accept')"><n-input :value="stringProp(field, 'accept')" placeholder=".pdf,.png,.jpg" @update:value="updateProp(index, 'accept', $event)" /></n-form-item>
        <n-form-item :label="t('workflow.form.maxSizeMb')"><n-input-number :value="numberProp(field, 'maxSizeMb')" :min="1" :max="100" @update:value="updateProp(index, 'maxSizeMb', $event)" /></n-form-item>
      </div>

      <div v-if="fieldErrors(index).length" class="wf-form-errors">{{ fieldErrors(index).join('；') }}</div>
    </n-card>
  </div>
</template>

<style scoped>
.wf-form-designer, .wf-form-field { display: flex; flex-direction: column; gap: var(--space-12); }
.wf-form-toolbar { display: flex; gap: var(--space-8); align-items: center; }
.wf-form-type-select { flex: 1; }
.wf-form-count { color: var(--color-text-tertiary); font-size: var(--font-size-sm); }
.wf-form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 var(--space-12); }
.wf-form-option { display: grid; grid-template-columns: 1fr 1fr auto; gap: var(--space-8); margin-bottom: var(--space-8); }
.wf-form-errors { color: var(--color-danger); font-size: var(--font-size-sm); }
</style>
