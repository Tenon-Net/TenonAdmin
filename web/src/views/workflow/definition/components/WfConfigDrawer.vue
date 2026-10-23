<script setup lang="ts">
/**
 * 节点配置抽屉。默认可见 ≤5 项,其余进「高级」(设计方案配置纪律)。
 * M3:内置表单存在时,审批高级配置字段权限;无内置表单时保留旧定义值但不生成权限。
 */
import { computed, reactive, watch, shallowRef, useTemplateRef } from 'vue'
import {
  NDrawer, NDrawerContent, NForm, NFormItem, NInput, NInputNumber, NSelect,
  NButton, NSpace, NCollapse, NCollapseItem, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import type {
  WfApprovalMode,
  WfModel,
} from '@/workflow/schema'
import { validateWfFormSchema } from '@/workflow/formSchema'
import { findNode, flattenChain } from '@/workflow/model'
import {
  applyNodeConfiguration,
  isWfWebhookUrl,
} from '@/workflow/configuration'
import WfConditionEditor from './WfConditionEditor.vue'
import WfApprovalAdvancedFields from './WfApprovalAdvancedFields.vue'
import WfAssigneeFields from './WfAssigneeFields.vue'
import WfStartFields from './WfStartFields.vue'
import WfWebhookFields from './WfWebhookFields.vue'
import { buildDrawerNodeConfig, loadDrawerForm, type WfDrawerFormState } from './drawerForm'

const props = defineProps<{
  show: boolean
  model: WfModel
  nodeId: string | null
}>()
const emit = defineEmits<{
  'update:show': [boolean]
  'update:model': [WfModel]
}>()

const { t } = useI18n()
const formRef = useTemplateRef<FormInst | null>('formRef')

const node = computed(() => (props.nodeId ? findNode(props.model.root, props.nodeId) : null))

const emptyNode = { id: '', type: 'start' as const, name: '', next: null }
const form = reactive<WfDrawerFormState>(loadDrawerForm({ version: 1, root: emptyNode }, emptyNode))
const expandedArm = shallowRef<string | number | null>(null)

const modeOptions = computed(() => (['any', 'all', 'seq'] as WfApprovalMode[]).map(
  (value) => ({ label: t(`workflow.mode.${value}`), value }),
))

const approvalMode = computed<WfApprovalMode>({
  get: () => form.assignee.provider === 'multiLeader' ? 'seq' : form.approval.mode,
  set: (value) => {
    form.approval.mode = value
    if (value === 'seq') form.approval.allPassRatio = 100
  },
})

const jumpNodeOptions = computed(() => {
  if (!node.value) return []
  return flattenChain(props.model.root)
    .filter((n) => n.id !== node.value!.id)
    .map((n) => ({
      label: n.name?.trim() || t(`workflow.node.${n.type}`),
      value: n.id,
    }))
})

const formPermissionFields = computed(() => {
  if (node.value?.type !== 'approval' || form.start.formComponent.trim()) return []
  return form.start.formSchema?.fields ?? []
})

const webhookRules = computed<FormRules>(() => ({
  webhook: {
    url: {
      trigger: ['input', 'blur'],
      validator: (_rule, value) => isWfWebhookUrl(String(value)),
      message: t('workflow.webhook.urlInvalid'),
    },
    timeoutSeconds: {
      required: true, type: 'number', min: 1, max: 120, message: t('workflow.webhook.timeoutRange'),
    },
    maxAttempts: {
      required: true, type: 'number', min: 1, max: 100, message: t('workflow.webhook.maxAttemptsRange'),
    },
  },
}))

const approvalRules = computed<FormRules>(() => ({
  approval: {
    allPassRatio: {
      required: true, type: 'number', min: 1, max: 100, message: t('workflow.designer.allPassRatioRange'),
    },
  },
}))

watch(
  () => [props.show, props.nodeId, props.model] as const,
  () => {
    if (!props.show || !node.value) return
    Object.assign(form, loadDrawerForm(props.model, node.value))
    expandedArm.value = node.value.type === 'branch'
      ? node.value.conditions?.find((arm) => !arm.isDefault)?.id ?? null
      : null
  },
  { immediate: true },
)

async function apply() {
  if (!props.nodeId || !node.value) return
  if (node.value.type === 'start' && form.start.formMode === 'schema' && validateWfFormSchema(form.start.formSchema).length) return
  if (node.value.type === 'webhook' || node.value.type === 'approval') {
    try {
      await formRef.value?.validate()
    } catch {
      return
    }
  }
  const config = buildDrawerNodeConfig(node.value, form)
  if (!config) return
  const next = applyNodeConfiguration(props.model, props.nodeId, config)
  if (!next) return
  emit('update:model', next)
  emit('update:show', false)
}

const title = computed(() => {
  if (!node.value) return t('workflow.designer.config')
  return t('workflow.designer.configTitle', { name: node.value.name || t(`workflow.node.${node.value.type}`) })
})
</script>

<template>
  <n-drawer :show="show" :width="node?.type === 'start' ? 560 : 400" placement="right" @update:show="emit('update:show', $event)">
    <n-drawer-content :title="title" closable>
      <n-form
        v-if="node"
        ref="formRef"
        :model="form"
        :rules="node.type === 'webhook' ? webhookRules : node.type === 'approval' ? approvalRules : undefined"
        class="wf-config-form"
        label-placement="top"
        size="medium"
      >
        <n-form-item :label="t('workflow.designer.nodeName')">
          <n-input v-model:value="form.name" :placeholder="t('workflow.designer.nodeName')" />
        </n-form-item>

        <WfStartFields v-if="node.type === 'start'" v-model="form.start" />

        <template v-else-if="node.type === 'branch'">
          <n-collapse
            v-model:expanded-names="expandedArm"
            accordion
            display-directive="if"
            class="wf-branch-conditions"
          >
            <n-collapse-item v-for="arm in node.conditions ?? []" :key="arm.id" :name="arm.id">
              <template #header>
                <span class="wf-branch-condition-title">{{ arm.name || t('workflow.designer.armName') }}</span>
              </template>
              <div v-if="arm.isDefault" class="wf-branch-default-hint">
                {{ t('workflow.condition.defaultHint') }}
              </div>
              <WfConditionEditor
                v-else
                v-model="form.armExpressions[arm.id]"
              />
            </n-collapse-item>
          </n-collapse>
        </template>

        <template v-else-if="node.type === 'parallel'">
          <p class="wf-parallel-hint">{{ t('workflow.designer.parallelHint') }}</p>
        </template>

        <WfWebhookFields v-else-if="node.type === 'webhook'" v-model="form.webhook" />

        <template v-else>
          <WfAssigneeFields v-model="form.assignee" :show-position-org="node.type === 'cc'" />

          <n-form-item v-if="node.type === 'approval'" :label="t('workflow.designer.mode')">
            <n-select
              v-model:value="approvalMode"
              :options="modeOptions"
              :disabled="form.assignee.provider === 'multiLeader'"
            />
          </n-form-item>
          <n-form-item
            v-if="node.type === 'approval' && approvalMode === 'all'"
            :label="t('workflow.designer.allPassRatio')"
            path="approval.allPassRatio"
          >
            <n-input-number v-model:value="form.approval.allPassRatio" :min="1" :max="100" :precision="0" class="w-full" />
          </n-form-item>
          <WfApprovalAdvancedFields
            v-if="node.type === 'approval'"
            v-model="form.approval"
            v-model:assignee="form.assignee"
            :form-fields="formPermissionFields"
            :jump-node-options="jumpNodeOptions"
          />
        </template>

      </n-form>

      <template #footer>
        <n-space justify="end">
          <n-button @click="emit('update:show', false)">{{ t('common.cancel') }}</n-button>
          <n-button type="primary" @click="apply">{{ t('common.save') }}</n-button>
        </n-space>
      </template>
    </n-drawer-content>
  </n-drawer>
</template>

<style scoped>
.w-full { width: 100%; }
.wf-config-form :deep(.wf-advanced) { margin-top: 4px; }
.wf-config-form :deep(.wf-advanced-title) {
  font-size: var(--font-size-sm);
  font-weight: 600;
  color: var(--color-text-secondary);
}
.wf-config-form :deep(.wf-advanced .n-collapse-item__header) { padding: 8px 0; }
.wf-config-form :deep(.wf-advanced .n-collapse-item__content-inner) { padding-top: 8px; }
.wf-branch-conditions {
  display: flex;
  flex-direction: column;
  gap: var(--space-12);
}
.wf-branch-condition-title {
  color: var(--color-text-primary);
  font-weight: 600;
}
.wf-branch-default-hint {
  padding: var(--space-8);
  border-radius: var(--radius-sm);
  background: var(--color-bg-body);
  color: var(--color-text-secondary);
  font-size: var(--font-size-sm);
}
.wf-parallel-hint {
  margin: 0;
  padding: var(--space-12);
  border-radius: var(--radius-sm);
  background: var(--color-warning-bg);
  color: var(--color-text-secondary);
  font-size: var(--font-size-sm);
}
</style>
