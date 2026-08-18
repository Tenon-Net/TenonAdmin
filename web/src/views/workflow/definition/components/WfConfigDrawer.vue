<script setup lang="ts">
/**
 * 节点配置抽屉。默认可见 ≤5 项,其余进「高级」(设计方案配置纪律)。
 * M1:start / approval / cc;不配分支/超时/字段权限。
 */
import { computed, reactive, watch } from 'vue'
import {
  NDrawer, NDrawerContent, NForm, NFormItem, NInput, NInputNumber, NSelect,
  NButton, NSpace, NCollapse, NCollapseItem,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import UserSelect from '@/components/UserSelect/index.vue'
import ApiSelect from '@/components/ApiSelect/index.vue'
import OrgTreeSelect from '@/components/OrgTreeSelect/index.vue'
import { roleApi, positionApi } from '@/api'
import type { WfAssigneeProvider, WfModel } from '@/workflow/schema'
import { cloneModel, findNode } from '@/workflow/model'

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

const node = computed(() => (props.nodeId ? findNode(props.model.root, props.nodeId) : null))

const form = reactive({
  name: '',
  formComponent: '' as string,
  provider: 'leader' as string,
  level: 1,
  userIds: [] as number[],
  roleId: null as number | null,
  positionId: null as number | null,
  positionOrgId: null as number | null,
  initiatorUserIds: [] as number[],
  initiatorRoleIds: [] as number[],
  initiatorOrgIds: [] as number[],
})

const providerOptions = computed(() =>
  (['user', 'leader', 'multiLeader', 'role', 'position', 'selfSelect', 'initiator', 'orgLeader'] as WfAssigneeProvider[]).map(
    (k) => ({ label: t(`workflow.provider.${k}`), value: k }),
  ),
)

const showAdvanced = computed(() => {
  if (!node.value) return false
  if (node.value.type === 'start') return true
  return form.provider === 'position'
})

watch(
  () => [props.show, props.nodeId, props.model] as const,
  () => {
    if (!props.show || !node.value) return
    const n = node.value
    form.name = n.name
    form.formComponent = props.model.formComponent ?? ''
    const a = n.props?.assignee
    form.provider = a?.provider || (n.type === 'cc' ? 'user' : 'leader')
    const params = a?.params ?? {}
    form.level = Number(params.level ?? 1) || 1
    const ids = params.userIds
    form.userIds = Array.isArray(ids) ? ids.map(Number).filter((x) => x > 0) : params.userId ? [Number(params.userId)] : []
    form.roleId = params.roleId != null ? Number(params.roleId) : Array.isArray(params.roleIds) ? Number(params.roleIds[0]) : null
    form.positionId = params.positionId != null ? Number(params.positionId) : null
    form.positionOrgId = params.orgId != null ? Number(params.orgId) : null
    const scope = n.props?.initiatorScope ?? []
    form.initiatorUserIds = scope.filter((x) => x.type === 'user').map((x) => x.id)
    form.initiatorRoleIds = scope.filter((x) => x.type === 'role').map((x) => x.id)
    form.initiatorOrgIds = scope.filter((x) => x.type === 'org').map((x) => x.id)
  },
  { immediate: true },
)

function buildAssigneeParams(): Record<string, unknown> {
  switch (form.provider) {
    case 'user':
      return { userIds: [...form.userIds] }
    case 'leader':
    case 'multiLeader':
      return { level: form.level }
    case 'role':
      return form.roleId ? { roleId: form.roleId } : {}
    case 'position':
      return form.positionId
        ? { positionId: form.positionId, ...(form.positionOrgId ? { orgId: form.positionOrgId } : {}) }
        : {}
    default:
      return {}
  }
}

function apply() {
  if (!props.nodeId || !node.value) return
  const next = cloneModel(props.model)
  const n = findNode(next.root, props.nodeId)
  if (!n) return
  n.name = form.name.trim() || n.name
  if (n.type === 'start') {
    next.formComponent = form.formComponent.trim() || null
    n.props = {
      ...n.props,
      initiatorScope: [
        ...form.initiatorUserIds.map((id) => ({ type: 'user' as const, id })),
        ...form.initiatorRoleIds.map((id) => ({ type: 'role' as const, id })),
        ...form.initiatorOrgIds.map((id) => ({ type: 'org' as const, id })),
      ],
    }
  } else {
    n.props = {
      ...n.props,
      assignee: { provider: form.provider, params: buildAssigneeParams() },
    }
    if (n.type === 'approval') {
      n.props.mode = 'any'
      n.props.nobody = 'autoPass'
      n.props.nobodyTransferUserId = undefined
      n.props.onReject = 'terminate'
      n.props.formPerms = n.props.formPerms ?? []
    }
  }
  emit('update:model', next)
  emit('update:show', false)
}

async function fetchRoles(keyword: string) {
  const { items } = await roleApi.page({ page: 1, pageSize: 50, name: keyword || undefined })
  return items.map((r) => ({ label: r.name, value: r.id }))
}

async function fetchPositions(keyword: string) {
  const { items } = await positionApi.page({ page: 1, pageSize: 50, name: keyword || undefined })
  return items.map((r) => ({ label: r.name, value: r.id }))
}

const title = computed(() => {
  if (!node.value) return t('workflow.designer.config')
  return t('workflow.designer.configTitle', { name: node.value.name || t(`workflow.node.${node.value.type}`) })
})
</script>

<template>
  <n-drawer :show="show" :width="400" placement="right" @update:show="emit('update:show', $event)">
    <n-drawer-content :title="title" closable>
      <n-form v-if="node" label-placement="top" size="medium">
        <n-form-item :label="t('workflow.designer.nodeName')">
          <n-input v-model:value="form.name" :placeholder="t('workflow.designer.nodeName')" />
        </n-form-item>

        <template v-if="node.type === 'start'">
          <n-form-item :label="t('workflow.designer.initiatorUsers')">
            <UserSelect v-model:value="form.initiatorUserIds" multiple clearable />
          </n-form-item>
        </template>

        <template v-else>
          <n-form-item :label="t('workflow.designer.assignee')">
            <n-select v-model:value="form.provider" :options="providerOptions" />
          </n-form-item>

          <n-form-item v-if="form.provider === 'user'" :label="t('workflow.designer.users')">
            <UserSelect v-model:value="form.userIds" multiple :placeholder="t('workflow.designer.users')" />
          </n-form-item>
          <n-form-item v-else-if="form.provider === 'leader' || form.provider === 'multiLeader'" :label="t('workflow.designer.level')">
            <n-input-number v-model:value="form.level" :min="1" :max="20" class="w-full" />
          </n-form-item>
          <n-form-item v-else-if="form.provider === 'role'" :label="t('workflow.designer.role')">
            <ApiSelect v-model:value="form.roleId" :fetch="fetchRoles" :placeholder="t('workflow.designer.role')" />
          </n-form-item>
          <n-form-item v-else-if="form.provider === 'position'" :label="t('workflow.designer.position')">
            <ApiSelect v-model:value="form.positionId" :fetch="fetchPositions" :placeholder="t('workflow.designer.position')" />
          </n-form-item>
        </template>

        <n-collapse v-if="showAdvanced" class="wf-advanced" display-directive="show">
          <n-collapse-item name="advanced">
            <template #header>
              <span class="wf-advanced-title">{{ t('workflow.designer.advanced') }}</span>
            </template>

            <template v-if="node.type === 'start'">
              <n-form-item :label="t('workflow.designer.initiatorRoles')">
                <ApiSelect v-model:value="form.initiatorRoleIds" multiple clearable :fetch="fetchRoles" />
              </n-form-item>
              <n-form-item
                :label="t('workflow.designer.initiatorOrgs')"
                :feedback="t('workflow.designer.initiatorScopeHint')"
              >
                <OrgTreeSelect v-model:value="form.initiatorOrgIds" multiple cascade checkable clearable />
              </n-form-item>
              <n-form-item :label="t('workflow.designer.formComponent')" :feedback="t('workflow.designer.formComponentHint')">
                <n-input v-model:value="form.formComponent" placeholder="views/biz/leave/form" />
              </n-form-item>
            </template>

            <n-form-item
              v-else-if="form.provider === 'position'"
              :label="t('workflow.designer.positionOrg')"
            >
              <OrgTreeSelect v-model:value="form.positionOrgId" clearable />
            </n-form-item>
          </n-collapse-item>
        </n-collapse>
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
.wf-advanced {
  margin-top: 4px;
}
.wf-advanced-title {
  font-size: var(--font-size-sm);
  font-weight: 600;
  color: var(--color-text-secondary);
}
.wf-advanced :deep(.n-collapse-item__header) {
  padding: 8px 0;
}
.wf-advanced :deep(.n-collapse-item__content-inner) {
  padding-top: 8px;
}
</style>
