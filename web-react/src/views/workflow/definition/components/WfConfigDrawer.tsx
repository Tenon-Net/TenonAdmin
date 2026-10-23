/**
 * 节点配置抽屉。默认可见 ≤5 项,其余进「高级」(设计方案配置纪律)。
 * M3:内置表单存在时,审批高级配置字段权限;无内置表单时保留旧定义值但不生成权限。
 * 对应 Vue 侧 WfConfigDrawer.vue。
 */
import { useEffect, useRef, useState } from 'react'
import { App, Form, Input } from 'antd'
import { useTranslation } from 'react-i18next'
import { FormContainer } from '@/components/FormContainer'
import {
  applyNodeConfiguration,
  createConditionGroup,
  isWfWebhookUrl,
  type WfEditorNodeConfig,
  type WfWebhookMethod,
} from '@/workflow/configuration'
import { serializeWfFormSchema, validateWfFormSchema } from '@/workflow/formSchema'
import { normalizeWfId, type WfId } from '@/workflow/id'
import { findNode } from '@/workflow/model'
import type {
  WfApprovalMode,
  WfConditionExpr,
  WfFormFieldPerm,
  WfModel,
  WfNode,
} from '@/workflow/schema'
import { WfConfigFields, formPermissionRows } from './WfConfigFields'
import type { WfConfigDrawerForm } from './WfConfigForm'
import './wf-designer.css'

export type { WfConfigDrawerForm } from './WfConfigForm'

const deepClone = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T

/** 条件表达式回显:叶子包一层组,保证编辑器根节点恒为组(与后端结构一致)。 */
function loadArmExpression(expression: WfConditionExpr | null | undefined): WfConditionExpr {
  const cloned = deepClone(expression ?? createConditionGroup())
  return cloned.children != null ? cloned : { ...createConditionGroup(), children: [cloned] }
}

/** 节点 → 表单态。纯函数,抽出来便于钉住回显语义(旧定义缺省值、multiLeader 强制 seq 等)。 */
export function loadDrawerForm(model: WfModel, node: WfNode): WfConfigDrawerForm {
  const formComponent = model.formComponent ?? ''
  const formSchema = model.formSchema ? deepClone(model.formSchema) : null
  const assignee = node.props?.assignee
  const params = assignee?.params ?? {}
  const ids = params.userIds
  const scope = node.props?.initiatorScope ?? []
  const method = (node.props?.webhookMethod ?? 'POST').toUpperCase() as WfWebhookMethod

  return {
    name: node.name,
    formComponent,
    formSchema,
    formMode: formSchema ? 'schema' : formComponent ? 'component' : 'none',
    formPerms: Object.fromEntries([
      ...(formSchema?.fields ?? []).map((field) => [field.key, 'editable'] as const),
      ...(node.props?.formPerms ?? [])
        .filter((permission): permission is WfFormFieldPerm => !!permission?.field)
        .map((permission) => [permission.field, permission.access ?? 'editable'] as const),
    ]),
    provider: assignee?.provider || (node.type === 'cc' ? 'user' : 'leader'),
    mode: assignee?.provider === 'multiLeader' ? 'seq' : node.props?.mode ?? 'any',
    allPassRatio: node.props?.allPassRatio ?? 100,
    level: Number(params.level ?? 1) || 1,
    userIds: Array.isArray(ids)
      ? ids.map(normalizeWfId).filter((id): id is WfId => id !== null)
      : normalizeWfId(params.userId) == null ? [] : [normalizeWfId(params.userId)!],
    roleId: normalizeWfId(params.roleId)
      ?? (Array.isArray(params.roleIds) ? normalizeWfId(params.roleIds[0]) : null),
    positionId: normalizeWfId(params.positionId),
    positionOrgId: normalizeWfId(params.orgId),
    initiatorUserIds: scope.filter((x) => x.type === 'user').map((x) => x.id),
    initiatorRoleIds: scope.filter((x) => x.type === 'role').map((x) => x.id),
    initiatorOrgIds: scope.filter((x) => x.type === 'org').map((x) => x.id),
    armExpressions: Object.fromEntries(
      (node.conditions ?? [])
        .filter((arm) => !arm.isDefault)
        .map((arm) => [arm.id, loadArmExpression(arm.expr)]),
    ),
    returnPolicy: node.props?.returnPolicy ?? 'prev',
    returnToNodeId: node.props?.returnToNodeId ?? null,
    onReject: node.props?.onReject ?? 'terminate',
    rejectToNodeId: node.props?.rejectToNodeId ?? null,
    timeoutHours: node.props?.timeout?.hours ?? 0,
    timeoutAction: node.props?.timeout?.action ?? 'remind',
    timeoutTransferUserId: node.props?.timeout?.transferUserId ?? null,
    labelApprove: node.props?.buttonLabels?.approve ?? '',
    labelReject: node.props?.buttonLabels?.reject ?? '',
    labelReturn: node.props?.buttonLabels?.return ?? '',
    labelTransfer: node.props?.buttonLabels?.transfer ?? '',
    labelDelegate: node.props?.buttonLabels?.delegate ?? '',
    labelUrge: node.props?.buttonLabels?.urge ?? '',
    webhookUrl: node.props?.webhookUrl ?? '',
    webhookMethod: method,
    webhookHeaders: Object.entries(node.props?.webhookHeaders ?? {}).map(([key, value]) => ({ key, value: value ?? '' })),
    webhookTimeoutSeconds: node.props?.webhookTimeoutSeconds ?? 30,
    webhookOnFailure: node.props?.webhookOnFailure ?? 'fail',
    maxAttempts: node.props?.maxAttempts ?? 3,
  }
}

/**
 * 数值/URL 边界校验 → `字段 → i18n 键`。Vue 侧走 n-form rules,这里抽成纯函数:
 * 既给字段挂内联报错,也作为 apply 的闸门(有条目就不落库)。
 */
export function validateDrawerForm(node: WfNode, form: WfConfigDrawerForm): Record<string, string> {
  const errors: Record<string, string> = {}
  if (node.type === 'webhook') {
    if (!isWfWebhookUrl(form.webhookUrl)) errors.webhookUrl = 'workflow.webhook.urlInvalid'
    if (!Number.isInteger(form.webhookTimeoutSeconds) || form.webhookTimeoutSeconds < 1 || form.webhookTimeoutSeconds > 120) {
      errors.webhookTimeoutSeconds = 'workflow.webhook.timeoutRange'
    }
    if (!Number.isInteger(form.maxAttempts) || form.maxAttempts < 1 || form.maxAttempts > 100) {
      errors.maxAttempts = 'workflow.webhook.maxAttemptsRange'
    }
  }
  if (node.type === 'approval') {
    const mode = form.provider === 'multiLeader' ? 'seq' : form.mode
    if (mode === 'all' && (!Number.isInteger(form.allPassRatio) || form.allPassRatio < 1 || form.allPassRatio > 100)) {
      errors.allPassRatio = 'workflow.designer.allPassRatioRange'
    }
  }
  if (node.type === 'start' && form.formMode === 'schema' && validateWfFormSchema(form.formSchema).length) {
    errors.formSchema = 'workflow.designer.invalid'
  }
  return errors
}

function assigneeParams(form: WfConfigDrawerForm): Record<string, unknown> {
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

/** 表单态 → `applyNodeConfiguration` 的入参。纯函数,与 Vue 侧 `apply()` 的组装段同语义。 */
export function buildNodeConfig(node: WfNode, form: WfConfigDrawerForm): WfEditorNodeConfig | null {
  const approvalMode: WfApprovalMode = form.provider === 'multiLeader' ? 'seq' : form.mode
  switch (node.type) {
    case 'start':
      return {
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
    case 'branch':
      return { type: 'branch', name: form.name, armExpressions: form.armExpressions }
    case 'parallel':
      return { type: 'parallel', name: form.name }
    case 'approval': {
      const rows = formPermissionRows(node, form)
      return {
        type: 'approval',
        name: form.name,
        assignee: { provider: form.provider, params: assigneeParams(form) },
        mode: approvalMode,
        allPassRatio: approvalMode === 'all' ? form.allPassRatio : approvalMode === 'seq' ? 100 : undefined,
        returnPolicy: form.returnPolicy,
        returnToNodeId: form.returnToNodeId ?? undefined,
        onReject: form.onReject,
        rejectToNodeId: form.rejectToNodeId ?? undefined,
        timeout: form.timeoutHours > 0
          ? {
            hours: form.timeoutHours,
            action: form.timeoutAction,
            transferUserId: form.timeoutAction === 'transfer' ? form.timeoutTransferUserId ?? undefined : undefined,
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
        // 无内置表单字段时不生成 formPerms:旧定义里的值原样留在 props 上,不被空数组洗掉。
        ...(rows.length
          ? { formPerms: rows.filter(({ access }) => access !== 'editable').map(({ field, access }) => ({ field: field.key, access })) }
          : {}),
      }
    }
    case 'cc':
      return {
        type: 'cc',
        name: form.name,
        assignee: { provider: form.provider, params: assigneeParams(form) },
      }
    case 'webhook':
      return {
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
    default:
      return null
  }
}

export interface WfConfigDrawerProps {
  open: boolean
  model: WfModel
  nodeId: string | null
  onOpenChange: (open: boolean) => void
  onModelChange: (model: WfModel) => void
}

export function WfConfigDrawer({ open, model, nodeId, onOpenChange, onModelChange }: WfConfigDrawerProps) {
  const { t } = useTranslation()
  const { message } = App.useApp()

  const node = nodeId ? findNode(model.root, nodeId) : null
  // 模型走 ref 读:回显 effect 只认「打开/换节点」,把 model 放进依赖会让每次编辑都把表单冲回模型态。
  const modelRef = useRef(model)
  modelRef.current = model

  const [form, setForm] = useState<WfConfigDrawerForm | null>(null)
  const set = (patch: Partial<WfConfigDrawerForm>) => setForm((current) => (current ? { ...current, ...patch } : current))

  useEffect(() => {
    if (!open || !nodeId) return
    const target = findNode(modelRef.current.root, nodeId)
    if (target) setForm(loadDrawerForm(modelRef.current, target))
  }, [open, nodeId])

  const title = node
    ? t('workflow.designer.configTitle', { name: node.name || t(`workflow.node.${node.type}`) })
    : t('workflow.designer.config')

  if (!node || !form) {
    return (
      <FormContainer
        open={open}
        onOpenChange={onOpenChange}
        title={title}
        variant="drawer"
        width={400}
      >
        <div />
      </FormContainer>
    )
  }

  const errors = validateDrawerForm(node, form)

  /** FormContainer 协议:返回 false 留在抽屉里(校验失败不能静默丢掉用户的输入)。 */
  const apply = (): boolean => {
    if (!nodeId) return false
    const invalid = Object.values(errors)[0]
    if (invalid) {
      message.warning(t(invalid))
      return false
    }
    const config = buildNodeConfig(node, form)
    const next = config && applyNodeConfiguration(model, nodeId, config)
    if (!next) {
      message.warning(t('workflow.designer.invalid'))
      return false
    }
    onModelChange(next)
    return true
  }

  return (
    <FormContainer
      open={open}
      onOpenChange={onOpenChange}
      title={title}
      variant="drawer"
      width={node.type === 'start' ? 560 : 400}
      confirmText={t('common.save')}
      onConfirm={apply}
    >
      <Form layout="vertical">
        <Form.Item label={t('workflow.designer.nodeName')}>
          <Input
            value={form.name}
            aria-label={t('workflow.designer.nodeName')}
            placeholder={t('workflow.designer.nodeName')}
            onChange={(e) => set({ name: e.target.value })}
          />
        </Form.Item>

        <WfConfigFields node={node} model={model} form={form} set={set} errors={errors} />
      </Form>
    </FormContainer>
  )
}
