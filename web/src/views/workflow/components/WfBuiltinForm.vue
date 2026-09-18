<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import {
  NDatePicker,
  NForm,
  NFormItem,
  NInput,
  NInputNumber,
  NSelect,
  NSpace,
  NTag,
  type SelectOption,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import FileUpload from '@/components/FileUpload/index.vue'
import UserSelect from '@/components/UserSelect/index.vue'
import type { FileUploadOutput } from '@/types/api'
import type { WfFormField, WfFormFieldPerm, WfFormOption, WfFormSchema } from '@/workflow/schema'
import {
  formFieldAccess,
  formFieldValue,
  validateWfFormValues,
  type WfFormValueIssue,
  type WfFormValues,
} from '@/workflow/formRuntime'

const props = defineProps<{
  schema: WfFormSchema
  modelValue: WfFormValues
  mode: 'start' | 'approve' | 'view'
  permissions?: WfFormFieldPerm[] | null
}>()
const emit = defineEmits<{ 'update:modelValue': [value: WfFormValues] }>()

const { t } = useI18n()
const values = reactive<WfFormValues>({ ...props.modelValue })
const issues = ref<WfFormValueIssue[]>([])
const visibleFields = computed(() => props.schema.fields.filter((field) => !isHidden(field)))

watch(
  () => props.modelValue,
  (next) => {
    for (const key of Object.keys(values)) delete values[key]
    Object.assign(values, next)
  },
  { deep: true },
)

function updateValue(field: WfFormField, value: unknown) {
  if (!canEdit(field)) return
  if (value === undefined) delete values[field.key]
  else values[field.key] = value
  issues.value = issues.value.filter((issue) => issue.key !== field.key)
  const next = { ...values }
  emit('update:modelValue', next)
}

function fieldIssue(field: WfFormField): WfFormValueIssue | undefined {
  return issues.value.find((issue) => issue.key === field.key)
}

function issueText(field: WfFormField): string | undefined {
  const issue = fieldIssue(field)
  return issue ? t(`workflow.form.runtime.${issue.code}`) : undefined
}

function valueFor(field: WfFormField): unknown {
  return formFieldValue(field, values)
}

function numberValue(field: WfFormField): number | null {
  const value = valueFor(field)
  return typeof value === 'number' ? value : null
}

function stringValue(field: WfFormField): string | null {
  const value = valueFor(field)
  return typeof value === 'string' ? value : null
}

function options(field: WfFormField): WfFormOption[] {
  return field.props && 'options' in field.props ? field.props.options : []
}

function userMultiple(field: WfFormField): boolean {
  return field.type === 'user' && field.props?.multiple === true
}

function userValue(field: WfFormField): number | string | number[] | string[] | null {
  const value = valueFor(field)
  return typeof value === 'number' || typeof value === 'string' || Array.isArray(value) ? value as number | string | number[] | string[] : null
}

function updateUser(field: WfFormField, value: unknown) {
  if (userMultiple(field) && Array.isArray(value)) {
    const maxSelected = field.type === 'user' ? (field.props?.maxSelected ?? 20) : value.length
    updateValue(field, value.slice(0, maxSelected))
    return
  }
  updateValue(field, value)
}

function dateValue(field: WfFormField): string | null {
  const value = stringValue(field)
  if (!value) return null
  // Naive DatePicker strictParse 要求 format(parse(v))===v；异地时区字符串需先归一到本地偏移
  if (field.type === 'datetime') return toLocalDatetimeValue(value)
  return value
}

function updateDate(field: WfFormField, value: string | [string, string] | null) {
  if (Array.isArray(value)) return
  updateValue(field, value || null)
}

/** 将任意带时区 ISO 转为当前本地时区下可被 Naive value-format XXX 严格回环的字符串。 */
function toLocalDatetimeValue(value: string): string | null {
  const ms = Date.parse(value)
  if (!Number.isFinite(ms)) return null
  const d = new Date(ms)
  const pad = (n: number) => String(n).padStart(2, '0')
  const ymd = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
  const hms = `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
  const offsetMin = -d.getTimezoneOffset()
  if (offsetMin === 0) return `${ymd}T${hms}Z`
  const sign = offsetMin > 0 ? '+' : '-'
  const abs = Math.abs(offsetMin)
  return `${ymd}T${hms}${sign}${pad(Math.floor(abs / 60))}:${pad(abs % 60)}`
}

function numericPrecision(field: WfFormField): number | undefined {
  if (field.type === 'money') return 2
  return field.type === 'number' && field.props ? field.props.precision : undefined
}

/**
 * 附件 Id 是后端 long 雪花:JSON 既可能给 number 也可能给十进制 string。
 * 判定与 formRuntime 内部的 isPositiveId 同规则,但**不做 Number() 强转** ——
 * 19 位雪花过一遍 Number() 就掉精度,存回去是另一个文件。
 */
function normalizeId(raw: unknown): number | string | null {
  if (typeof raw === 'number') return Number.isSafeInteger(raw) && raw > 0 ? raw : null
  if (typeof raw === 'string' && /^[1-9]\d*$/.test(raw.trim())) return raw.trim()
  return null
}

function attachmentIds(field: WfFormField): Array<number | string> {
  const value = valueFor(field)
  const raw = field.type === 'attachment' && field.props?.multiple === true
    ? Array.isArray(value) ? value : []
    : [value]
  return raw.map(normalizeId).filter((id): id is number | string => id !== null)
}

function attachmentMaxCount(field: WfFormField): number {
  return field.type === 'attachment' && field.props?.multiple === true ? (field.props.maxCount ?? 20) : 1
}

function updateAttachment(field: WfFormField, output: FileUploadOutput) {
  const id = normalizeId(output.id)
  if (id === null) return
  if (field.type !== 'attachment' || field.props?.multiple !== true) {
    updateValue(field, id)
    return
  }
  const maxCount = field.props?.maxCount ?? 20
  const ids = attachmentIds(field)
  if (!ids.some((current) => String(current) === String(id))) updateValue(field, [...ids, id].slice(0, maxCount))
}

function removeAttachment(field: WfFormField, id: number | string) {
  const ids = attachmentIds(field).filter((current) => String(current) !== String(id))
  updateValue(field, field.type === 'attachment' && field.props?.multiple === true ? ids : null)
}

function validate(): boolean {
  issues.value = validateWfFormValues(props.schema, values, props.mode === 'start' ? undefined : props.permissions)
  return issues.value.length === 0
}

function canEdit(field: WfFormField): boolean {
  return props.mode === 'start' || (props.mode === 'approve' && formFieldAccess(field.key, props.permissions) === 'editable')
}

function isHidden(field: WfFormField): boolean {
  return props.mode !== 'start' && formFieldAccess(field.key, props.permissions) === 'hidden'
}

defineExpose({ validate })
</script>

<template>
  <n-form class="wf-builtin-form" :show-label="true" label-placement="top">
    <n-form-item
      v-for="field in visibleFields"
      :key="field.key"
      :label="field.label || field.key"
      :required="field.required && canEdit(field)"
      :feedback="issueText(field)"
      :validation-status="fieldIssue(field) ? 'error' : undefined"
    >
      <n-input
        v-if="field.type === 'text'"
        :value="stringValue(field)"
        :maxlength="field.props?.maxLength ?? 256"
        :placeholder="field.placeholder ?? undefined"
        :disabled="!canEdit(field)"
        @update:value="updateValue(field, $event)"
      />
      <n-input
        v-else-if="field.type === 'textarea'"
        :value="stringValue(field)"
        type="textarea"
        :rows="field.props?.rows ?? 4"
        :maxlength="field.props?.maxLength ?? 4000"
        :placeholder="field.placeholder ?? undefined"
        :disabled="!canEdit(field)"
        @update:value="updateValue(field, $event)"
      />
      <n-input-number
        v-else-if="field.type === 'number' || field.type === 'money'"
        :value="numberValue(field)"
        :min="field.props?.min"
        :max="field.props?.max"
        :precision="numericPrecision(field)"
        :placeholder="field.placeholder ?? undefined"
        :disabled="!canEdit(field)"
        class="w-full"
        @update:value="updateValue(field, $event)"
      />
      <n-date-picker
        v-else-if="field.type === 'date'"
        type="date"
        value-format="yyyy-MM-dd"
        clearable
        :formatted-value="dateValue(field)"
        :placeholder="field.placeholder ?? undefined"
        :disabled="!canEdit(field)"
        class="w-full"
        @update:formatted-value="updateDate(field, $event)"
      />
      <n-date-picker
        v-else-if="field.type === 'datetime'"
        type="datetime"
        value-format="yyyy-MM-dd'T'HH:mm:ssXXX"
        clearable
        :formatted-value="dateValue(field)"
        :placeholder="field.placeholder ?? undefined"
        :disabled="!canEdit(field)"
        class="w-full"
        @update:formatted-value="updateDate(field, $event)"
      />
      <n-select
        v-else-if="field.type === 'select' || field.type === 'multiSelect'"
        :value="valueFor(field) as string | string[] | null"
        :options="options(field) as unknown as SelectOption[]"
        :multiple="field.type === 'multiSelect'"
        :max-tag-count="field.type === 'multiSelect' ? field.props?.maxSelected : undefined"
        :placeholder="field.placeholder ?? undefined"
        :disabled="!canEdit(field)"
        @update:value="updateValue(field, $event)"
      />
      <UserSelect
        v-else-if="field.type === 'user'"
        :value="userValue(field)"
        :multiple="userMultiple(field)"
        :placeholder="field.placeholder ?? undefined"
        :disabled="!canEdit(field)"
        @update:value="updateUser(field, $event)"
      />
      <template v-else-if="field.type === 'attachment'">
        <FileUpload
          v-if="canEdit(field) && attachmentIds(field).length < attachmentMaxCount(field)"
          :key="`${field.key}:${attachmentIds(field).join(',')}`"
          :accept="field.props?.accept"
          :multiple="field.props?.multiple === true"
          :max="attachmentMaxCount(field) - attachmentIds(field).length"
          :show-file-list="true"
          @uploaded="updateAttachment(field, $event)"
        />
        <n-space v-if="attachmentIds(field).length" :size="8">
          <n-tag v-for="id in attachmentIds(field)" :key="String(id)" size="small" :closable="canEdit(field)" @close="removeAttachment(field, id)">{{ id }}
          </n-tag>
        </n-space>
        <span v-else-if="!canEdit(field)">—</span>
      </template>
    </n-form-item>
  </n-form>
</template>

<style scoped>
.wf-builtin-form {
  width: 100%;
}
.w-full {
  width: 100%;
}
</style>
