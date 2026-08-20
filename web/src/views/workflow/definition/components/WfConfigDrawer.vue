<script setup lang="ts">
/**
 * 节点配置抽屉。默认可见 ≤5 项,其余进「高级」(设计方案配置纪律)。
 * M2a:start / approval / cc / branch;不配超时/字段权限。
 */
import { computed, reactive, ref, watch } from 'vue'
import {
  NDrawer, NDrawerContent, NForm, NFormItem, NInput, NInputNumber, NSelect,
  NButton, NSpace, NCollapse, NCollapseItem,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import UserSelect from '@/components/UserSelect/index.vue'
import ApiSelect from '@/components/ApiSelect/index.vue'
import OrgTreeSelect from '@/components/OrgTreeSelect/index.vue'
import { roleApi, positionApi } from '@/api'
import type {
  WfApprovalMode,
  WfAssigneeProvider,
  WfConditionExpr,
  WfModel,
} from '@/workflow/schema'
import { findNode } from '@/workflow/model'
import {
  applyNodeConfiguration,
  createConditionGroup,
  type WfEditorNodeConfig,
} from '@/workflow/configuration'
import WfConditionEditor from './WfConditionEditor.vue'

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
  mode: 'any' as WfApprovalMode,
  level: 1,
  userIds: [] as number[],
  roleId: null as number | null,
  positionId: null as number | null,
  positionOrgId: null as number | null,
  initiatorUserIds: [] as number[],
  initiatorRoleIds: [] as number[],
  initiatorOrgIds: [] as number[],
  armExpressions: {} as Record<string, WfConditionExpr>,
})
const expandedArm = ref<string | number | null>(null)

const providerOptions = computed(() =>
  (['user', 'leader', 'multiLeader', 'role', 'position', 'selfSelect', 'initiator', 'orgLeader'] as WfAssigneeProvider[]).map(
    (k) => ({ label: t(`workflow.provider.${k}`), value: k }),
  ),
)

const modeOptions = computed(() => (['any', 'all', 'seq'] as WfApprovalMode[]).map(
  (value) => ({ label: t(`workflow.mode.${value}`), value }),
))

const approvalMode = computed<WfApprovalMode>({
  get: () => form.provider === 'multiLeader' ? 'seq' : form.mode,
  set: (value) => { form.mode = value },
})

const showAdvanced = computed(() => {
  if (!node.value) return false
  if (node.value.type === 'start') return true
  if (node.value.type === 'branch') return false
  return form.provider === 'position'
})

function loadArmExpression(expression: WfConditionExpr | null | undefined): WfConditionExpr {
  const cloned = JSON.parse(JSON.stringify(expression ?? createConditionGroup())) as WfConditionExpr
  return cloned.children != null
    ? cloned
    : { ...createConditionGroup(), children: [cloned] }
}

watch(
  () => [props.show, props.nodeId, props.model] as const,
  () => {
    if (!props.show || !node.value) return
    const n = node.value
    form.name = n.name
    form.formComponent = props.model.formComponent ?? ''
    const a = n.props?.assignee
    form.provider = a?.provider || (n.type === 'cc' ? 'user' : 'leader')
    form.mode = a?.provider === 'multiLeader' ? 'seq' : n.props?.mode ?? 'any'
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
    form.armExpressions = Object.fromEntries(
      (n.conditions ?? [])
        .filter((arm) => !arm.isDefault)
        .map((arm) => [
          arm.id,
          loadArmExpression(arm.expr),
        ]),
    )
    expandedArm.value = n.type === 'branch'
      ? n.conditions?.find((arm) => !arm.isDefault)?.id ?? null
      : null
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
  let config: WfEditorNodeConfig
  if (node.value.type === 'start') {
    config = {
      type: 'start',
      name: form.name,
      formComponent: form.formComponent,
      initiatorScope: [
        ...form.initiatorUserIds.map((id) => ({ type: 'user' as const, id })),
        ...form.initiatorRoleIds.map((id) => ({ type: 'role' as const, id })),
        ...form.initiatorOrgIds.map((id) => ({ type: 'org' as const, id })),
      ],
    }
  } else if (node.value.type === 'branch') {
    config = { type: 'branch', name: form.name, armExpressions: form.armExpressions }
  } else if (node.value.type === 'approval') {
    config = {
      type: 'approval',
      name: form.name,
      assignee: { provider: form.provider, params: buildAssigneeParams() },
      mode: approvalMode.value,
    }
  } else if (node.value.type === 'cc') {
    config = {
      type: 'cc',
      name: form.name,
      assignee: { provider: form.provider, params: buildAssigneeParams() },
    }
  } else {
    return
  }
  const next = applyNodeConfiguration(props.model, props.nodeId, config)
  if (!next) return
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

          <n-form-item v-if="node.type === 'approval'" :label="t('workflow.designer.mode')">
            <n-select
              v-model:value="approvalMode"
              :options="modeOptions"
              :disabled="form.provider === 'multiLeader'"
            />
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
</style>
