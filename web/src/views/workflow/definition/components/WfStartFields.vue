<script setup lang="ts">
import { NCollapse, NCollapseItem, NFormItem, NInput, NRadioButton, NRadioGroup } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { roleApi } from '@/api'
import ApiSelect from '@/components/ApiSelect/index.vue'
import OrgTreeSelect from '@/components/OrgTreeSelect/index.vue'
import UserSelect from '@/components/UserSelect/index.vue'
import WfFormDesigner from './WfFormDesigner.vue'
import type { WfStartFieldsState } from './drawerForm'

const model = defineModel<WfStartFieldsState>({ required: true })
const { t } = useI18n()

function update<K extends keyof WfStartFieldsState>(key: K, value: WfStartFieldsState[K]) {
  model.value = { ...model.value, [key]: value }
}

async function fetchRoles(keyword: string) {
  const { items } = await roleApi.page({ page: 1, pageSize: 50, name: keyword || undefined })
  return items.map((role) => ({ label: role.name, value: role.id }))
}
</script>

<template>
  <n-form-item :label="t('workflow.designer.initiatorUsers')">
    <UserSelect :value="model.initiatorUserIds" source="workflow" multiple clearable @update:value="update('initiatorUserIds', $event)" />
  </n-form-item>
  <n-collapse class="wf-advanced" display-directive="show">
    <n-collapse-item name="advanced">
      <template #header>
        <span class="wf-advanced-title">{{ t('workflow.designer.advanced') }}</span>
      </template>
      <n-form-item :label="t('workflow.designer.initiatorRoles')">
        <ApiSelect :value="model.initiatorRoleIds" multiple clearable :fetch="fetchRoles" @update:value="update('initiatorRoleIds', $event)" />
      </n-form-item>
      <n-form-item :label="t('workflow.designer.initiatorOrgs')" :feedback="t('workflow.designer.initiatorScopeHint')">
        <OrgTreeSelect :value="model.initiatorOrgIds" multiple cascade checkable clearable @update:value="update('initiatorOrgIds', $event)" />
      </n-form-item>
      <n-form-item :label="t('workflow.form.mode')">
        <n-radio-group :value="model.formMode" @update:value="update('formMode', $event)">
          <n-radio-button value="none">{{ t('workflow.form.modeNone') }}</n-radio-button>
          <n-radio-button value="schema">{{ t('workflow.form.modeSchema') }}</n-radio-button>
          <n-radio-button value="component">{{ t('workflow.form.modeComponent') }}</n-radio-button>
        </n-radio-group>
      </n-form-item>
      <WfFormDesigner v-if="model.formMode === 'schema'" :model-value="model.formSchema" @update:model-value="update('formSchema', $event)" />
      <n-form-item
        v-else-if="model.formMode === 'component'"
        :label="t('workflow.designer.formComponent')"
        :feedback="t('workflow.designer.formComponentHint')"
      >
        <n-input :value="model.formComponent" maxlength="256" placeholder="views/biz/leave/form" @update:value="update('formComponent', $event)" />
      </n-form-item>
    </n-collapse-item>
  </n-collapse>
</template>
