<script setup lang="ts">
/**
 * 发起流程页。菜单 component 填 `workflow/start/index`;
 * 可选 `?definitionId=` 预选已发布定义。含 formComponent 挂载点 + 摘要变量(键值行)。
 */
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
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
import type { WfStartableDefinitionDetail } from '@/types/workflow'
import { flattenChain } from '@/workflow/model'
import type { WfModel } from '@/workflow/schema'
import WfFormMount from '../components/WfFormMount.vue'

interface VarRow {
  key: string
  value: string
}

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const message = useMessage()

const formRef = ref<FormInst | null>(null)
const submitting = ref(false)
const defsLoading = ref(false)
const defOptions = ref<{ label: string; value: number }[]>([])
const formComponent = ref<string | null>(null)
const snapshot = ref<WfStartableDefinitionDetail | null>(null)

const form = reactive({
  definitionId: null as number | null,
  businessKey: '',
  varRows: [{ key: '', value: '' }] as VarRow[],
  selectedUserIdsByNode: {} as Record<string, number[]>,
})

const rules: FormRules = {
  definitionId: {
    required: true,
    type: 'number',
    message: () => t('workflow.start.definitionRequired'),
    trigger: ['change', 'blur'],
  },
}

const hasFormMount = computed(() => !!formComponent.value?.trim())
const selfSelectNodes = computed(() => {
  const model = snapshot.value?.model as WfModel | undefined
  if (!model?.root) return []
  return flattenChain(model.root).filter((n) => n.props?.assignee?.provider === 'selfSelect')
})

const variablesJson = computed(() => serializeVars(form.varRows))

function coerceValue(raw: string): unknown {
  const s = raw.trim()
  if (s === '') return ''
  if (s === 'true') return true
  if (s === 'false') return false
  if (/^-?\d+(\.\d+)?$/.test(s)) return Number(s)
  return raw
}

function serializeVars(rows: VarRow[]): string | null {
  const obj: Record<string, unknown> = {}
  for (const row of rows) {
    const k = row.key.trim()
    if (!k) continue
    obj[k] = coerceValue(row.value)
  }
  return Object.keys(obj).length ? JSON.stringify(obj) : null
}

async function loadPublishedDefs() {
  defsLoading.value = true
  try {
    defOptions.value = (await wfInstanceApi.startable())
      .map((d) => ({ label: d.name ?? '', value: Number(d.id) }))
      .filter((d) => d.value > 0)
  } catch (e) {
    message.error(translateError(e))
  } finally {
    defsLoading.value = false
  }
}

async function onDefinitionChange(id: number | null) {
  formComponent.value = null
  snapshot.value = null
  form.selectedUserIdsByNode = {}
  form.varRows = [{ key: '', value: '' }]
  if (!id) return
  try {
    const detail = await wfInstanceApi.startableDetail(id)
    snapshot.value = detail
    formComponent.value = detail.model?.formComponent ?? detail.formComponent ?? null
  } catch (e) {
    message.error(translateError(e))
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
  const q = Number(route.query.definitionId)
  if (Number.isFinite(q) && q > 0) {
    form.definitionId = q
  }
})

async function submit() {
  try {
    await formRef.value?.validate()
  } catch {
    return
  }
  if (!form.definitionId) return

  submitting.value = true
  try {
    const result = await wfInstanceApi.start({
      definitionId: form.definitionId!,
      businessKey: form.businessKey.trim() || null,
      variablesJson: variablesJson.value,
      selectedUserIdsByNode: Object.fromEntries(
        selfSelectNodes.value
          .map((node) => [node.id, form.selectedUserIdsByNode[node.id] ?? []] as const)
          .filter(([, ids]) => ids.length > 0),
      ),
    })
    message.success(t('workflow.start.started'))
    await router.push(`/workflow/instance/${result.instanceId}/detail`)
  } catch (e) {
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
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="120" style="max-width: 640px">
      <n-form-item :label="t('workflow.start.definition')" path="definitionId">
        <n-select
          v-model:value="form.definitionId"
          :options="defOptions"
          :loading="defsLoading"
          filterable
          :placeholder="t('workflow.start.definitionPlaceholder')"
        />
      </n-form-item>
      <n-form-item :label="t('workflow.start.businessKey')">
        <n-input v-model:value="form.businessKey" :placeholder="t('workflow.start.businessKeyHint')" />
      </n-form-item>
      <n-form-item :label="t('workflow.start.variables')">
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
          v-model:value="form.selectedUserIdsByNode[node.id]"
          multiple
          :placeholder="t('workflow.start.selfSelectHint')"
        />
      </n-form-item>
    </n-form>

    <WfFormMount
      v-if="hasFormMount"
      :form-component="formComponent"
      mode="start"
      :definition-id="form.definitionId ?? undefined"
      :business-key="form.businessKey || null"
      :variables-json="variablesJson"
    />

    <n-space style="margin-top: 16px">
      <n-button type="primary" :loading="submitting" @click="submit">
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
