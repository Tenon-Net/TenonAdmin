<script setup lang="ts">
/**
 * 节点配置抽屉。默认可见 ≤5 项,其余进「高级」(设计方案配置纪律)。
 * M3:内置表单存在时,审批高级配置字段权限;无内置表单时保留旧定义值但不生成权限。
 */
import { computed, reactive, ref, watch } from 'vue'
import {
  NDrawer, NDrawerContent, NForm, NFormItem, NInput, NInputNumber, NSelect,
  NButton, NSpace, NCollapse, NCollapseItem, NRadioButton, NRadioGroup, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import UserSelect from '@/components/UserSelect/index.vue'
import ApiSelect from '@/components/ApiSelect/index.vue'
import OrgTreeSelect from '@/components/OrgTreeSelect/index.vue'
import { roleApi, positionApi } from '@/api'
import type {
  WfApprovalMode,
  WfAssigneeProvider,
  WfConditionExpr,
  WfModel,
  WfRejectAction,
  WfReturnPolicy,
  WfTimeoutAction,
  WfWebhookFailureAction,
  WfFormFieldPerm,
  WfFormPermAccess,
  WfFormSchema,
} from '@/workflow/schema'
import { serializeWfFormSchema, validateWfFormSchema } from '@/workflow/formSchema'
import { findNode, flattenChain } from '@/workflow/model'
import {
  applyNodeConfiguration,
  createConditionGroup,
  isWfWebhookUrl,
  WF_WEBHOOK_METHODS,
  type WfEditorNodeConfig,
  type WfWebhookMethod,
} from '@/workflow/configuration'
import WfConditionEditor from './WfConditionEditor.vue'
import WfFormDesigner from './WfFormDesigner.vue'

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
const formRef = ref<FormInst | null>(null)

const node = computed(() => (props.nodeId ? findNode(props.model.root, props.nodeId) : null))

const form = reactive({
  name: '',
  formComponent: '' as string,
  formMode: 'none' as 'none' | 'schema' | 'component',
  formSchema: null as WfFormSchema | null,
  formPerms: {} as Record<string, WfFormPermAccess>,
  provider: 'leader' as string,
  mode: 'any' as WfApprovalMode,
  allPassRatio: 100,
  level: 1,
  userIds: [] as number[],
  roleId: null as number | null,
  positionId: null as number | null,
  positionOrgId: null as number | null,
  initiatorUserIds: [] as Array<number | string>,
  initiatorRoleIds: [] as Array<number | string>,
  initiatorOrgIds: [] as Array<number | string>,
  armExpressions: {} as Record<string, WfConditionExpr>,
  returnPolicy: 'prev' as WfReturnPolicy,
  returnToNodeId: null as string | null,
  onReject: 'terminate' as WfRejectAction,
  rejectToNodeId: null as string | null,
  timeoutHours: 0,
  timeoutAction: 'remind' as WfTimeoutAction,
  timeoutTransferUserId: null as number | string | null,
  labelApprove: '',
  labelReject: '',
  labelReturn: '',
  labelTransfer: '',
  labelDelegate: '',
  labelUrge: '',
  webhookUrl: '',
  webhookMethod: 'POST' as WfWebhookMethod,
  webhookHeaders: [] as Array<{ key: string; value: string }>,
  webhookTimeoutSeconds: 30,
  webhookOnFailure: 'fail' as WfWebhookFailureAction,
  maxAttempts: 3,
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
  set: (value) => {
    form.mode = value
    if (value === 'seq') form.allPassRatio = 100
  },
})

const showAdvanced = computed(() => {
  if (!node.value) return false
  if (node.value.type === 'start' || node.value.type === 'approval' || node.value.type === 'webhook') return true
  if (node.value.type === 'branch') return false
  return form.provider === 'position'
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

const returnPolicyOptions = computed(() => (['prev', 'any', 'node'] as WfReturnPolicy[]).map(
  (value) => ({ label: t(`workflow.returnPolicy.${value}`), value }),
))

const onRejectOptions = computed(() => (['terminate', 'toNode'] as WfRejectAction[]).map(
  (value) => ({ label: t(`workflow.onReject.${value}`), value }),
))

const timeoutActionOptions = computed(() => (['remind', 'autoPass', 'autoReject', 'transfer'] as WfTimeoutAction[]).map(
  (value) => ({ label: t(`workflow.timeoutAction.${value}`), value }),
))

const webhookMethodOptions = WF_WEBHOOK_METHODS.map(
  (value) => ({ label: value, value }),
)

const webhookFailureOptions = computed(() => (['fail', 'manual'] as WfWebhookFailureAction[]).map(
  (value) => ({ label: t(`workflow.webhook.failure.${value}`), value }),
))

const formPermissionOptions = computed(() => (['editable', 'readonly', 'hidden'] as WfFormPermAccess[]).map(
  (value) => ({ label: t(`workflow.form.permission.${value}`), value }),
))

const formPermissionFields = computed(() => {
  if (node.value?.type !== 'approval' || form.formComponent.trim() || !form.formSchema?.fields.length) return []
  return form.formSchema.fields.map((field) => ({
    field,
    access: form.formPerms[field.key] ?? 'editable' as WfFormPermAccess,
  }))
})

const webhookRules = computed<FormRules>(() => ({
  webhookUrl: {
    trigger: ['input', 'blur'],
    validator: (_rule, value) => isWfWebhookUrl(String(value)),
    message: t('workflow.webhook.urlInvalid'),
  },
  webhookTimeoutSeconds: {
    type: 'number', min: 1, max: 120, message: t('workflow.webhook.timeoutRange'),
  },
  maxAttempts: {
    type: 'number', min: 1, max: 100, message: t('workflow.webhook.maxAttemptsRange'),
  },
}))

const approvalRules = computed<FormRules>(() => ({
  allPassRatio: {
    type: 'number', min: 1, max: 100, message: t('workflow.designer.allPassRatioRange'),
  },
}))

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
    form.formSchema = props.model.formSchema
      ? JSON.parse(JSON.stringify(props.model.formSchema)) as WfFormSchema
      : null
    form.formMode = form.formSchema ? 'schema' : form.formComponent ? 'component' : 'none'
    form.formPerms = Object.fromEntries([
      ...(form.formSchema?.fields ?? []).map((field) => [field.key, 'editable'] as const),
      ...(n.props?.formPerms ?? [])
        .filter((permission): permission is WfFormFieldPerm => !!permission?.field)
        .map((permission) => [permission.field, permission.access ?? 'editable'] as const),
    ])
    const a = n.props?.assignee
    form.provider = a?.provider || (n.type === 'cc' ? 'user' : 'leader')
    form.mode = a?.provider === 'multiLeader' ? 'seq' : n.props?.mode ?? 'any'
    form.allPassRatio = n.props?.allPassRatio ?? 100
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
    form.returnPolicy = n.props?.returnPolicy ?? 'prev'
    form.returnToNodeId = n.props?.returnToNodeId ?? null
    form.onReject = n.props?.onReject ?? 'terminate'
    form.rejectToNodeId = n.props?.rejectToNodeId ?? null
    form.timeoutHours = n.props?.timeout?.hours ?? 0
    form.timeoutAction = n.props?.timeout?.action ?? 'remind'
    form.timeoutTransferUserId = n.props?.timeout?.transferUserId ?? null
    form.labelApprove = n.props?.buttonLabels?.approve ?? ''
    form.labelReject = n.props?.buttonLabels?.reject ?? ''
    form.labelReturn = n.props?.buttonLabels?.return ?? ''
    form.labelTransfer = n.props?.buttonLabels?.transfer ?? ''
    form.labelDelegate = n.props?.buttonLabels?.delegate ?? ''
    form.labelUrge = n.props?.buttonLabels?.urge ?? ''
    form.webhookUrl = n.props?.webhookUrl ?? ''
    const method = (n.props?.webhookMethod ?? 'POST').toUpperCase()
    form.webhookMethod = method as WfWebhookMethod
    form.webhookHeaders = Object.entries(n.props?.webhookHeaders ?? {}).map(([key, value]) => ({
      key,
      value: value ?? '',
    }))
    form.webhookTimeoutSeconds = n.props?.webhookTimeoutSeconds ?? 30
    form.webhookOnFailure = n.props?.webhookOnFailure ?? 'fail'
    form.maxAttempts = n.props?.maxAttempts ?? 3
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

async function apply() {
  if (!props.nodeId || !node.value) return
  if (node.value.type === 'start' && form.formMode === 'schema' && validateWfFormSchema(form.formSchema).length) return
  if (node.value.type === 'webhook') {
    try {
      await formRef.value?.validate()
    } catch {
      return
    }
  }
  let config: WfEditorNodeConfig
  if (node.value.type === 'start') {
    config = {
      type: 'start',
      name: form.name,
      formComponent: form.formMode === 'component' ? form.formComponent : null,
      formSchema: form.formMode === 'schema' ? serializeWfFormSchema(form.formSchema) : null,
      initiatorScope: [
        ...form.initiatorUserIds.map((id) => ({ type: 'user' as const, id })),
        ...form.initiatorRoleIds.map((id) => ({ type: 'role' as const, id })),
        ...form.initiatorOrgIds.map((id) => ({ type: 'org' as const, id })),
      ],
    }
  } else if (node.value.type === 'branch') {
    config = { type: 'branch', name: form.name, armExpressions: form.armExpressions }
  } else if (node.value.type === 'parallel') {
    config = { type: 'parallel', name: form.name }
  } else if (node.value.type === 'approval') {
    config = {
      type: 'approval',
      name: form.name,
      assignee: { provider: form.provider, params: buildAssigneeParams() },
      mode: approvalMode.value,
      allPassRatio: approvalMode.value === 'all' ? form.allPassRatio : approvalMode.value === 'seq' ? 100 : undefined,
      returnPolicy: form.returnPolicy,
      returnToNodeId: form.returnToNodeId ?? undefined,
      onReject: form.onReject,
      rejectToNodeId: form.rejectToNodeId ?? undefined,
      timeout: form.timeoutHours > 0
        ? {
            hours: form.timeoutHours,
            action: form.timeoutAction,
            transferUserId: form.timeoutAction === 'transfer'
              ? form.timeoutTransferUserId ?? undefined
              : undefined,
          }
        : undefined,
      buttonLabels: {
        approve: form.labelApprove,
        reject: form.labelReject,
        return: form.labelReturn,
        transfer: form.labelTransfer,
        delegate: form.labelDelegate,
        urge: form.labelUrge,
      },
      ...(formPermissionFields.value.length
        ? {
            formPerms: formPermissionFields.value
              .filter(({ access }) => access !== 'editable')
              .map(({ field, access }) => ({ field: field.key, access })),
          }
        : {}),
    }
  } else if (node.value.type === 'cc') {
    config = {
      type: 'cc',
      name: form.name,
      assignee: { provider: form.provider, params: buildAssigneeParams() },
    }
  } else if (node.value.type === 'webhook') {
    config = {
      type: 'webhook',
      name: form.name,
      webhookUrl: form.webhookUrl,
      webhookMethod: form.webhookMethod,
      webhookHeaders: Object.fromEntries(
        form.webhookHeaders.filter(({ key }) => key.trim()).map(({ key, value }) => [key.trim(), value]),
      ),
      webhookTimeoutSeconds: form.webhookTimeoutSeconds,
      webhookOnFailure: form.webhookOnFailure,
      maxAttempts: form.maxAttempts,
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
  <n-drawer :show="show" :width="node?.type === 'start' ? 560 : 400" placement="right" @update:show="emit('update:show', $event)">
    <n-drawer-content :title="title" closable>
      <n-form
        v-if="node"
        ref="formRef"
        :model="form"
        :rules="node.type === 'webhook' ? webhookRules : node.type === 'approval' ? approvalRules : undefined"
        label-placement="top"
        size="medium"
      >
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

        <template v-else-if="node.type === 'parallel'">
          <p class="wf-parallel-hint">{{ t('workflow.designer.parallelHint') }}</p>
        </template>

        <template v-else-if="node.type === 'webhook'">
          <n-form-item :label="t('workflow.webhook.url')" path="webhookUrl">
            <n-input v-model:value="form.webhookUrl" :placeholder="t('workflow.webhook.urlPlaceholder')" />
          </n-form-item>
          <n-form-item :label="t('workflow.webhook.method')">
            <n-select v-model:value="form.webhookMethod" :options="webhookMethodOptions" />
          </n-form-item>
          <n-form-item :label="t('workflow.webhook.timeoutSeconds')" path="webhookTimeoutSeconds">
            <n-input-number v-model:value="form.webhookTimeoutSeconds" :min="1" :max="120" :precision="0" class="w-full" />
          </n-form-item>
          <n-form-item :label="t('workflow.webhook.onFailure')">
            <n-select v-model:value="form.webhookOnFailure" :options="webhookFailureOptions" />
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

          <n-form-item v-if="node.type === 'approval'" :label="t('workflow.designer.mode')">
            <n-select
              v-model:value="approvalMode"
              :options="modeOptions"
              :disabled="form.provider === 'multiLeader'"
            />
          </n-form-item>
          <n-form-item
            v-if="node.type === 'approval' && approvalMode === 'all'"
            :label="t('workflow.designer.allPassRatio')"
            path="allPassRatio"
          >
            <n-input-number v-model:value="form.allPassRatio" :min="1" :max="100" :precision="0" class="w-full" />
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
              <n-form-item :label="t('workflow.form.mode')">
                <n-radio-group v-model:value="form.formMode">
                  <n-radio-button value="none">{{ t('workflow.form.modeNone') }}</n-radio-button>
                  <n-radio-button value="schema">{{ t('workflow.form.modeSchema') }}</n-radio-button>
                  <n-radio-button value="component">{{ t('workflow.form.modeComponent') }}</n-radio-button>
                </n-radio-group>
              </n-form-item>
              <WfFormDesigner v-if="form.formMode === 'schema'" v-model="form.formSchema" />
              <n-form-item v-else-if="form.formMode === 'component'" :label="t('workflow.designer.formComponent')" :feedback="t('workflow.designer.formComponentHint')">
                <n-input v-model:value="form.formComponent" maxlength="256" placeholder="views/biz/leave/form" />
              </n-form-item>
            </template>

            <n-form-item
              v-else-if="form.provider === 'position'"
              :label="t('workflow.designer.positionOrg')"
            >
              <OrgTreeSelect v-model:value="form.positionOrgId" clearable />
            </n-form-item>

            <template v-if="node.type === 'approval'">
              <div v-if="formPermissionFields.length" class="wf-form-permissions">
                <n-form-item
                  v-for="{ field } in formPermissionFields"
                  :key="field.key"
                  :label="field.label || field.key"
                >
                  <n-select v-model:value="form.formPerms[field.key]" :options="formPermissionOptions" />
                </n-form-item>
              </div>
              <n-form-item :label="t('workflow.designer.returnPolicy')">
                <n-select v-model:value="form.returnPolicy" :options="returnPolicyOptions" />
              </n-form-item>
              <n-form-item v-if="form.returnPolicy === 'node'" :label="t('workflow.designer.returnToNode')">
                <n-select v-model:value="form.returnToNodeId" :options="jumpNodeOptions" clearable />
              </n-form-item>
              <n-form-item :label="t('workflow.designer.onReject')">
                <n-select v-model:value="form.onReject" :options="onRejectOptions" />
              </n-form-item>
              <n-form-item v-if="form.onReject === 'toNode'" :label="t('workflow.designer.rejectToNode')">
                <n-select v-model:value="form.rejectToNodeId" :options="jumpNodeOptions" clearable />
              </n-form-item>
              <n-form-item :label="t('workflow.designer.timeoutHours')">
                <n-input-number v-model:value="form.timeoutHours" :min="0" :max="8760" class="w-full" />
              </n-form-item>
              <n-form-item v-if="form.timeoutHours > 0" :label="t('workflow.designer.timeoutAction')">
                <n-select v-model:value="form.timeoutAction" :options="timeoutActionOptions" />
              </n-form-item>
              <n-form-item
                v-if="form.timeoutHours > 0 && form.timeoutAction === 'transfer'"
                :label="t('workflow.designer.timeoutTransfer')"
              >
                <UserSelect v-model:value="form.timeoutTransferUserId" />
              </n-form-item>
              <n-form-item :label="t('workflow.designer.labelApprove')">
                <n-input v-model:value="form.labelApprove" :placeholder="t('workflow.detail.approve')" />
              </n-form-item>
              <n-form-item :label="t('workflow.designer.labelReject')">
                <n-input v-model:value="form.labelReject" :placeholder="t('workflow.detail.reject')" />
              </n-form-item>
              <n-form-item :label="t('workflow.designer.labelReturn')">
                <n-input v-model:value="form.labelReturn" :placeholder="t('workflow.detail.return')" />
              </n-form-item>
              <n-form-item :label="t('workflow.designer.labelTransfer')">
                <n-input v-model:value="form.labelTransfer" :placeholder="t('workflow.detail.transfer')" />
              </n-form-item>
              <n-form-item :label="t('workflow.designer.labelDelegate')">
                <n-input v-model:value="form.labelDelegate" :placeholder="t('workflow.detail.delegate')" />
              </n-form-item>
              <n-form-item :label="t('workflow.designer.labelUrge')">
                <n-input v-model:value="form.labelUrge" :placeholder="t('workflow.detail.urge')" />
              </n-form-item>
            </template>

            <template v-else-if="node.type === 'webhook'">
              <n-form-item :label="t('workflow.webhook.headers')">
                <div class="wf-webhook-headers">
                  <div v-for="(header, index) in form.webhookHeaders" :key="index" class="wf-webhook-header">
                    <n-input v-model:value="header.key" :placeholder="t('workflow.webhook.headerName')" />
                    <n-input v-model:value="header.value" :placeholder="t('workflow.webhook.headerValue')" />
                    <n-button
                      quaternary
                      circle
                      :aria-label="t('workflow.webhook.removeHeader')"
                      :title="t('workflow.webhook.removeHeader')"
                      @click="form.webhookHeaders.splice(index, 1)"
                    >
                      <template #icon><AppIcon icon="ph:x" :size="14" /></template>
                    </n-button>
                  </div>
                  <n-button dashed block @click="form.webhookHeaders.push({ key: '', value: '' })">
                    <template #icon><AppIcon icon="ph:plus" :size="14" /></template>
                    {{ t('workflow.webhook.addHeader') }}
                  </n-button>
                </div>
              </n-form-item>
              <n-form-item :label="t('workflow.webhook.maxAttempts')" path="maxAttempts">
                <n-input-number v-model:value="form.maxAttempts" :min="1" :max="100" :precision="0" class="w-full" />
              </n-form-item>
            </template>
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
.wf-parallel-hint {
  margin: 0;
  padding: var(--space-12);
  border-radius: var(--radius-sm);
  background: var(--color-warning-bg);
  color: var(--color-text-secondary);
  font-size: var(--font-size-sm);
}
.wf-form-permissions {
  padding-bottom: var(--space-8);
}
.wf-webhook-headers {
  display: flex;
  flex-direction: column;
  gap: var(--space-8);
  width: 100%;
}
.wf-webhook-header {
  display: grid;
  grid-template-columns: 1fr 1fr auto;
  gap: var(--space-8);
  align-items: center;
}
</style>
