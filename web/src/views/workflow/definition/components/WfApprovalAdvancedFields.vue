<script setup lang="ts">
import { computed } from 'vue'
import { NCollapse, NCollapseItem, NFormItem, NInput, NInputNumber, NSelect } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import OrgTreeSelect from '@/components/OrgTreeSelect/index.vue'
import UserSelect from '@/components/UserSelect/index.vue'
import type { WfFormField, WfFormPermAccess, WfRejectAction, WfReturnPolicy, WfTimeoutAction } from '@/workflow/schema'
import type { WfApprovalAdvancedState, WfAssigneeFieldsState } from './drawerForm'

defineProps<{
  formFields: WfFormField[]
  jumpNodeOptions: Array<{ label: string; value: string }>
}>()
const model = defineModel<WfApprovalAdvancedState>({ required: true })
const assignee = defineModel<WfAssigneeFieldsState>('assignee', { required: true })
const { t } = useI18n()

function update<K extends keyof WfApprovalAdvancedState>(key: K, value: WfApprovalAdvancedState[K]) {
  model.value = { ...model.value, [key]: value }
}

function updateAssignee<K extends keyof WfAssigneeFieldsState>(key: K, value: WfAssigneeFieldsState[K]) {
  assignee.value = { ...assignee.value, [key]: value }
}

function updatePermission(field: string, access: WfFormPermAccess) {
  update('formPerms', { ...model.value.formPerms, [field]: access })
}

const permissionOptions = computed(() => (['editable', 'readonly', 'hidden'] as WfFormPermAccess[]).map(
  (value) => ({ label: t(`workflow.form.permission.${value}`), value }),
))
const returnPolicyOptions = computed(() => (['prev', 'any', 'node'] as WfReturnPolicy[]).map(
  (value) => ({ label: t(`workflow.returnPolicy.${value}`), value }),
))
const rejectOptions = computed(() => (['terminate', 'toNode'] as WfRejectAction[]).map(
  (value) => ({ label: t(`workflow.onReject.${value}`), value }),
))
const timeoutOptions = computed(() => (['remind', 'autoPass', 'autoReject', 'transfer'] as WfTimeoutAction[]).map(
  (value) => ({ label: t(`workflow.timeoutAction.${value}`), value }),
))
</script>

<template>
  <n-collapse class="wf-advanced" display-directive="show">
    <n-collapse-item name="advanced">
      <template #header>
        <span class="wf-advanced-title">{{ t('workflow.designer.advanced') }}</span>
      </template>
      <n-form-item v-if="assignee.provider === 'position'" :label="t('workflow.designer.positionOrg')">
        <OrgTreeSelect :value="assignee.positionOrgId" clearable @update:value="updateAssignee('positionOrgId', $event)" />
      </n-form-item>
      <div v-if="formFields.length" class="wf-form-permissions">
        <n-form-item v-for="field in formFields" :key="field.key" :label="field.label || field.key">
          <n-select :value="model.formPerms[field.key]" :options="permissionOptions" @update:value="updatePermission(field.key, $event)" />
        </n-form-item>
      </div>
      <n-form-item :label="t('workflow.designer.returnPolicy')">
        <n-select :value="model.returnPolicy" :options="returnPolicyOptions" @update:value="update('returnPolicy', $event)" />
      </n-form-item>
      <n-form-item v-if="model.returnPolicy === 'node'" :label="t('workflow.designer.returnToNode')">
        <n-select :value="model.returnToNodeId" :options="jumpNodeOptions" clearable @update:value="update('returnToNodeId', $event)" />
      </n-form-item>
      <n-form-item :label="t('workflow.designer.onReject')">
        <n-select :value="model.onReject" :options="rejectOptions" @update:value="update('onReject', $event)" />
      </n-form-item>
      <n-form-item v-if="model.onReject === 'toNode'" :label="t('workflow.designer.rejectToNode')">
        <n-select :value="model.rejectToNodeId" :options="jumpNodeOptions" clearable @update:value="update('rejectToNodeId', $event)" />
      </n-form-item>
      <n-form-item :label="t('workflow.designer.timeoutHours')">
        <n-input-number :value="model.timeoutHours" :min="0" :max="8760" class="w-full" @update:value="update('timeoutHours', $event ?? 0)" />
      </n-form-item>
      <n-form-item v-if="model.timeoutHours > 0" :label="t('workflow.designer.timeoutAction')">
        <n-select :value="model.timeoutAction" :options="timeoutOptions" @update:value="update('timeoutAction', $event)" />
      </n-form-item>
      <n-form-item v-if="model.timeoutHours > 0 && model.timeoutAction === 'transfer'" :label="t('workflow.designer.timeoutTransfer')">
        <UserSelect :value="model.timeoutTransferUserId" source="workflow" @update:value="update('timeoutTransferUserId', $event)" />
      </n-form-item>
      <n-form-item :label="t('workflow.designer.labelApprove')">
        <n-input :value="model.labelApprove" :placeholder="t('workflow.detail.approve')" @update:value="update('labelApprove', $event)" />
      </n-form-item>
      <n-form-item :label="t('workflow.designer.labelReject')">
        <n-input :value="model.labelReject" :placeholder="t('workflow.detail.reject')" @update:value="update('labelReject', $event)" />
      </n-form-item>
      <n-form-item :label="t('workflow.designer.labelReturn')">
        <n-input :value="model.labelReturn" :placeholder="t('workflow.detail.return')" @update:value="update('labelReturn', $event)" />
      </n-form-item>
      <n-form-item :label="t('workflow.designer.labelTransfer')">
        <n-input :value="model.labelTransfer" :placeholder="t('workflow.detail.transfer')" @update:value="update('labelTransfer', $event)" />
      </n-form-item>
      <n-form-item :label="t('workflow.designer.labelDelegate')">
        <n-input :value="model.labelDelegate" :placeholder="t('workflow.detail.delegate')" @update:value="update('labelDelegate', $event)" />
      </n-form-item>
      <n-form-item :label="t('workflow.designer.labelUrge')">
        <n-input :value="model.labelUrge" :placeholder="t('workflow.detail.urge')" @update:value="update('labelUrge', $event)" />
      </n-form-item>
    </n-collapse-item>
  </n-collapse>
</template>

<style scoped>
.w-full { width: 100%; }
.wf-form-permissions { padding-bottom: var(--space-8); }
</style>
