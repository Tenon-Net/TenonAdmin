<script setup lang="ts">
import { computed } from 'vue'
import { NCollapse, NCollapseItem, NFormItem, NInputNumber, NSelect } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { positionApi, roleApi } from '@/api'
import ApiSelect from '@/components/ApiSelect/index.vue'
import OrgTreeSelect from '@/components/OrgTreeSelect/index.vue'
import UserSelect from '@/components/UserSelect/index.vue'
import type { WfAssigneeProvider } from '@/workflow/schema'
import type { WfAssigneeFieldsState } from './drawerForm'

const model = defineModel<WfAssigneeFieldsState>({ required: true })
defineProps<{ showPositionOrg?: boolean }>()
const { t } = useI18n()

function update<K extends keyof WfAssigneeFieldsState>(key: K, value: WfAssigneeFieldsState[K]) {
  model.value = { ...model.value, [key]: value }
}

const providerOptions = computed(() =>
  (['user', 'leader', 'multiLeader', 'role', 'position', 'selfSelect', 'initiator', 'orgLeader'] as WfAssigneeProvider[])
    .map((value) => ({ label: t(`workflow.provider.${value}`), value })),
)

async function fetchRoles(keyword: string) {
  const { items } = await roleApi.page({ page: 1, pageSize: 50, name: keyword || undefined })
  return items.map((role) => ({ label: role.name, value: role.id }))
}

async function fetchPositions(keyword: string) {
  const { items } = await positionApi.page({ page: 1, pageSize: 50, name: keyword || undefined })
  return items.map((position) => ({ label: position.name, value: position.id }))
}
</script>

<template>
  <n-form-item :label="t('workflow.designer.assignee')">
    <n-select :value="model.provider" :options="providerOptions" @update:value="update('provider', $event)" />
  </n-form-item>
  <n-form-item v-if="model.provider === 'user'" :label="t('workflow.designer.users')">
    <UserSelect :value="model.userIds" source="workflow" multiple :placeholder="t('workflow.designer.users')" @update:value="update('userIds', $event)" />
  </n-form-item>
  <n-form-item v-else-if="model.provider === 'leader' || model.provider === 'multiLeader'" :label="t('workflow.designer.level')">
    <n-input-number :value="model.level" :min="1" :max="20" class="w-full" @update:value="update('level', $event ?? 1)" />
  </n-form-item>
  <n-form-item v-else-if="model.provider === 'role'" :label="t('workflow.designer.role')">
    <ApiSelect :value="model.roleId" :fetch="fetchRoles" :placeholder="t('workflow.designer.role')" @update:value="update('roleId', $event)" />
  </n-form-item>
  <n-form-item v-else-if="model.provider === 'position'" :label="t('workflow.designer.position')">
    <ApiSelect :value="model.positionId" :fetch="fetchPositions" :placeholder="t('workflow.designer.position')" @update:value="update('positionId', $event)" />
  </n-form-item>
  <n-collapse v-if="showPositionOrg && model.provider === 'position'" class="wf-advanced" display-directive="show">
    <n-collapse-item name="advanced">
      <template #header><span class="wf-advanced-title">{{ t('workflow.designer.advanced') }}</span></template>
      <n-form-item :label="t('workflow.designer.positionOrg')">
        <OrgTreeSelect :value="model.positionOrgId" clearable @update:value="update('positionOrgId', $event)" />
      </n-form-item>
    </n-collapse-item>
  </n-collapse>
</template>

<style scoped>
.w-full { width: 100%; }
</style>
