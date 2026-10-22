<script setup lang="ts">
/**
 * 发起流程页。菜单 component 填 `workflow/start/index`;
 * 可选 `?definitionId=` 预选已发布定义。含 formComponent 挂载点 + 摘要变量(键值行)。
 */
import { computed, onMounted, reactive, ref, watch, shallowRef, useTemplateRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NAlert,
  NButton,
  NCard,
  NForm,
  NFormItem,
  NInput,
  NSelect,
  NSpace,
  useMessage,
  type FormInst,
  type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import UserSelect from '@/components/UserSelect/index.vue'
import { wfInstanceApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import type { WfStartableDefinition, WfStartableDefinitionDetail } from '@/types/workflow'
import type { WfId } from '@/workflow/id'
import { isPositiveWfId, normalizeWfId, wfInstanceDetailPath } from '@/workflow/id'
import { parseQueryApiId } from '@/utils/id'
import { flattenChain } from '@/workflow/model'
import { projectWfRuntimeModel } from '@/workflow/formSchema'
import type { WfFormSchema } from '@/workflow/schema'
import { classifyOutcome, useRequestKey } from '@/workflow/useRequestKey'
import WfFormMount from '../components/WfFormMount.vue'

interface VarRow {
  key: string
  value: string
}

const { t, te } = useI18n()
const route = useRoute()
const router = useRouter()
const message = useMessage()

const formRef = useTemplateRef<FormInst | null>('formRef')
const submitting = shallowRef(false)
const defsLoading = shallowRef(false)
const detailLoading = shallowRef(false)
const defOptions = ref<{ label: string; value: WfId; disabled?: boolean }[]>([])
const formComponent = shallowRef<string | null>(null)
const formSchema = ref<WfFormSchema | null>(null)
const runtimeVariablesJson = shallowRef<string | null>(null)
const formRuntimeRef = useTemplateRef<{ validate: () => boolean } | null>('formRuntimeRef')
const snapshot = ref<WfStartableDefinitionDetail | null>(null)
const requestKey = useRequestKey()
let detailRequest = 0

const form = reactive({
  definitionId: null as WfId | null,
  businessKey: '',
  varRows: [{ key: '', value: '' }] as VarRow[],
  selectedUserIdsByNode: {} as Record<string, WfId[]>,
})

const rules: FormRules = {
  definitionId: {
    required: true,
    validator: (_rule, value) => isPositiveWfId(value),
    message: () => t('workflow.start.definitionRequired'),
    trigger: ['change', 'blur'],
  },
}

const hasFormMount = computed(() => !!formComponent.value?.trim())
const hasBuiltinForm = computed(() => !!formSchema.value?.fields.length)
const selfSelectNodes = computed(() => {
  const model = projectWfRuntimeModel(snapshot.value?.model)
  if (!model?.root) return []
  return flattenChain(model.root).filter((n) => n.props?.assignee?.provider === 'selfSelect')
})

const variablesJson = computed(() => serializeVars(form.varRows))

function canonicalDecimal(value: string): string {
  const negative = value.startsWith('-')
  const unsigned = negative ? value.slice(1) : value
  const [integerPart, fractionPart] = unsigned.split('.')
  const integer = integerPart!.replace(/^0+(?=\d)/, '')
  const fraction = fractionPart?.replace(/0+$/, '') ?? ''
  const magnitude = fraction ? `${integer}.${fraction}` : integer
  return negative && magnitude !== '0' ? `-${magnitude}` : magnitude
}

function coerceValue(raw: string): unknown {
  const s = raw.trim()
  if (s === '') return ''
  if (s === 'true') return true
  if (s === 'false') return false
  if (/^-?\d+(?:\.\d+)?$/.test(s)) {
    const canonical = canonicalDecimal(s)
    const value = Number(s)
    if (!Number.isFinite(value) || String(value) !== canonical) return raw
    if (canonical === '0') return 0
    return canonical.includes('.') || Number.isSafeInteger(value) ? value : raw
  }
  return raw
}

function serializeVars(rows: VarRow[]): string | null {
  const obj: Record<string, unknown> = Object.create(null) as Record<string, unknown>
  for (const row of rows) {
    const k = row.key.trim()
    if (!k || k === '__proto__') continue
    obj[k] = coerceValue(row.value)
  }
  return Object.keys(obj).length ? JSON.stringify(obj) : null
}

function startableOption(d: WfStartableDefinition) {
  const reason = d.unavailableReason
  const issueKey = reason ? `workflow.designer.issue.${reason}` : ''
  const message = issueKey && te(issueKey) ? t(issueKey) : ''
  const node = (d.unavailableNodeName || d.unavailableNodeId || '').trim()
  const detail = node && message ? `${node}：${message}` : message || node
  return {
    label: detail ? t('workflow.start.unavailable', { name: d.name ?? '', detail }) : (d.name ?? ''),
    value: normalizeWfId(d.id),
    disabled: Boolean(reason),
  }
}

async function loadPublishedDefs() {
  defsLoading.value = true
  try {
    defOptions.value = (await wfInstanceApi.startable())
      .map((d) => startableOption(d))
      .filter((d): d is ReturnType<typeof startableOption> & { value: WfId } => d.value != null)
  } catch (e) {
    message.error(translateError(e))
  } finally {
    defsLoading.value = false
  }
}

async function onDefinitionChange(id: WfId | null) {
  const request = ++detailRequest
  formComponent.value = null
  formSchema.value = null
  runtimeVariablesJson.value = null
  snapshot.value = null
  form.selectedUserIdsByNode = {}
  form.varRows = [{ key: '', value: '' }]
  detailLoading.value = !!id
  if (!id) return
  try {
    const detail = await wfInstanceApi.startableDetail(id)
    if (request !== detailRequest) return
    snapshot.value = detail
    formComponent.value = detail.model?.formComponent ?? detail.formComponent ?? null
    formSchema.value = projectWfRuntimeModel(detail.model)?.formSchema ?? null
  } catch (e) {
    if (request === detailRequest) message.error(translateError(e))
  } finally {
    if (request === detailRequest) detailLoading.value = false
  }
}

watch(
  () => form.definitionId,
  (id) => {
    void onDefinitionChange(id)
  },
)

onMounted(async () => {
  await loadPublishedDefs()
  const id = parseQueryApiId(route.query.definitionId)
  if (id != null) form.definitionId = id
})

async function submit() {
  if (detailLoading.value || !snapshot.value) return
  try {
    await formRef.value?.validate()
  } catch {
    return
  }
  if (!form.definitionId) return
  if (hasBuiltinForm.value && !formRuntimeRef.value?.validate()) return

  submitting.value = true
  try {
    const body = {
      definitionId: form.definitionId!,
      businessKey: form.businessKey.trim() || null,
      variablesJson: hasBuiltinForm.value ? runtimeVariablesJson.value : variablesJson.value,
      selectedUserIdsByNode: Object.fromEntries(
        selfSelectNodes.value
          .map((node) => [node.id, form.selectedUserIdsByNode[node.id] ?? []] as const)
          .filter(([, ids]) => ids.length > 0),
      ),
      requestId: requestKey.value(),
    }
    const result = await wfInstanceApi.start(body)
    requestKey.settle('success')
    message.success(t('workflow.start.started'))
    const path = wfInstanceDetailPath(result.instanceId)
    if (path) await router.push(path)
  } catch (e) {
    requestKey.settle(classifyOutcome(e))
    message.error(translateError(e))
  } finally {
    submitting.value = false
  }
}

function addVarRow() {
  form.varRows.push({ key: '', value: '' })
}

function removeVarRow(i: number) {
  form.varRows.splice(i, 1)
  if (!form.varRows.length) form.varRows.push({ key: '', value: '' })
}
</script>

<template>
  <n-card :title="t('workflow.start.title')" :bordered="false" size="small">
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="top" style="max-width: 640px">
      <n-form-item :label="t('workflow.start.definition')" path="definitionId">
        <n-select
          v-model:value="form.definitionId"
          :options="defOptions"
          :loading="defsLoading"
          filterable
          :placeholder="t('workflow.start.definitionPlaceholder')"
        />
      </n-form-item>
      <n-alert v-if="snapshot?.mayBlockWithoutApprover" type="warning" :bordered="false" style="margin-bottom: 12px">
        {{ t('workflow.start.mayBlock') }}
      </n-alert>
      <n-form-item :label="t('workflow.start.businessKey')">
        <n-input v-model:value="form.businessKey" :placeholder="t('workflow.start.businessKeyHint')" />
      </n-form-item>
      <n-form-item v-if="!hasBuiltinForm" :label="t('workflow.start.variables')">
        <div class="wf-kv">
          <div v-for="(row, i) in form.varRows" :key="i" class="wf-kv-row">
            <n-input v-model:value="row.key" :placeholder="t('workflow.start.varKey')" />
            <n-input v-model:value="row.value" :placeholder="t('workflow.start.varValue')" />
            <n-button quaternary circle size="small" :aria-label="t('common.delete')" @click="removeVarRow(i)">
              <template #icon><AppIcon icon="ph:x" :size="14" /></template>
            </n-button>
          </div>
          <n-button dashed size="small" @click="addVarRow">
            <template #icon><AppIcon icon="ph:plus" :size="14" /></template>
            {{ t('workflow.start.addVariable') }}
          </n-button>
          <p class="wf-kv-hint">{{ t('workflow.start.variablesHint') }}</p>
        </div>
      </n-form-item>
      <n-form-item
        v-for="node in selfSelectNodes"
        :key="node.id"
        :label="node.name || t('workflow.start.selfSelect')"
      >
        <UserSelect
          source="workflow"
          v-model:value="form.selectedUserIdsByNode[node.id]"
          multiple
          :placeholder="t('workflow.start.selfSelectHint')"
        />
      </n-form-item>
    </n-form>

    <WfFormMount
      v-if="hasFormMount || hasBuiltinForm"
      ref="formRuntimeRef"
      :form-component="formComponent"
      :form-schema="formSchema"
      mode="start"
      :definition-id="form.definitionId ?? undefined"
      :business-key="form.businessKey || null"
      :variables-json="hasBuiltinForm ? runtimeVariablesJson : variablesJson"
      @variables-change="runtimeVariablesJson = $event"
    />

    <n-space style="margin-top: 16px">
      <n-button type="primary" :loading="submitting" :disabled="detailLoading || !snapshot" @click="submit">
        {{ t('workflow.start.submit') }}
      </n-button>
    </n-space>
  </n-card>
</template>

<style scoped>
.wf-kv {
  display: flex;
  flex-direction: column;
  gap: 8px;
  width: 100%;
}
.wf-kv-row {
  display: flex;
  gap: 8px;
  align-items: center;
}
.wf-kv-hint {
  margin: 0;
  font-size: var(--font-size-xs);
  color: var(--color-text-tertiary);
  line-height: var(--line-height-xs);
}
</style>
