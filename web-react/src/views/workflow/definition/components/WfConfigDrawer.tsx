/**
 * 节点配置抽屉。默认可见 ≤5 项,其余进「高级」(设计方案配置纪律)。
 * M3:内置表单存在时,审批高级配置字段权限;无内置表单时保留旧定义值但不生成权限。
 * 对应 Vue 侧 WfConfigDrawer.vue。
 */
import { useEffect, useMemo, useRef, useState } from 'react'
import { App, Button, Collapse, Form, Input, InputNumber, Radio, Select } from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { ApiSelect, type ApiFetch } from '@/components/ApiSelect'
import { FormContainer } from '@/components/FormContainer'
import { OrgTreeSelect } from '@/components/OrgTreeSelect'
import { UserSelect } from '@/components/UserSelect'
import { positionApi, roleApi } from '@/api'
import {
  WF_WEBHOOK_METHODS,
  applyNodeConfiguration,
  createConditionGroup,
  isWfWebhookUrl,
  type WfEditorNodeConfig,
  type WfWebhookMethod,
} from '@/workflow/configuration'
import { serializeWfFormSchema, validateWfFormSchema } from '@/workflow/formSchema'
import { normalizeWfId, type WfId } from '@/workflow/id'
import { findNode, flattenChain } from '@/workflow/model'
import type {
  WfApprovalMode,
  WfAssigneeProvider,
  WfConditionExpr,
  WfFormFieldPerm,
  WfFormPermAccess,
  WfFormSchema,
  WfModel,
  WfNode,
  WfRejectAction,
  WfReturnPolicy,
  WfTimeoutAction,
  WfWebhookFailureAction,
} from '@/workflow/schema'
import { WfConditionEditor } from './WfConditionEditor'
import { WfFormDesigner } from './WfFormDesigner'
import './wf-designer.css'

type WfFormMode = 'none' | 'schema' | 'component'

export interface WfConfigDrawerForm {
  name: string
  formComponent: string
  formMode: WfFormMode
  formSchema: WfFormSchema | null
  formPerms: Record<string, WfFormPermAccess>
  provider: string
  mode: WfApprovalMode
  allPassRatio: number
  level: number
  userIds: WfId[]
  roleId: WfId | null
  positionId: WfId | null
  positionOrgId: WfId | null
  initiatorUserIds: WfId[]
  initiatorRoleIds: WfId[]
  initiatorOrgIds: WfId[]
  armExpressions: Record<string, WfConditionExpr>
  returnPolicy: WfReturnPolicy
  returnToNodeId: string | null
  onReject: WfRejectAction
  rejectToNodeId: string | null
  timeoutHours: number
  timeoutAction: WfTimeoutAction
  timeoutTransferUserId: WfId | null
  labelApprove: string
  labelReject: string
  labelReturn: string
  labelTransfer: string
  labelDelegate: string
  labelUrge: string
  webhookUrl: string
  webhookMethod: WfWebhookMethod
  webhookHeaders: Array<{ key: string; value: string }>
  webhookTimeoutSeconds: number
  webhookOnFailure: WfWebhookFailureAction
  maxAttempts: number
}

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

/** 审批节点的字段权限行;无内置表单(或走业务挂载点)时为空,此时 apply 不生成 formPerms。 */
export function formPermissionRows(node: WfNode, form: WfConfigDrawerForm) {
  if (node.type !== 'approval' || form.formComponent.trim() || !form.formSchema?.fields.length) return []
  return form.formSchema.fields.map((field) => ({
    field,
    access: form.formPerms[field.key] ?? ('editable' as WfFormPermAccess),
  }))
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

const fetchRoles: ApiFetch = async (keyword) => {
  const { items } = await roleApi.page({ page: 1, pageSize: 50, name: keyword || undefined })
  return items.map((r) => ({ label: r.name, value: r.id }))
}

const fetchPositions: ApiFetch = async (keyword) => {
  const { items } = await positionApi.page({ page: 1, pageSize: 50, name: keyword || undefined })
  return items.map((r) => ({ label: r.name, value: r.id }))
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

  const providerOptions = useMemo(
    () => (['user', 'leader', 'multiLeader', 'role', 'position', 'selfSelect', 'initiator', 'orgLeader'] as WfAssigneeProvider[])
      .map((value) => ({ label: t(`workflow.provider.${value}`), value })),
    [t],
  )
  const modeOptions = useMemo(
    () => (['any', 'all', 'seq'] as WfApprovalMode[]).map((value) => ({ label: t(`workflow.mode.${value}`), value })),
    [t],
  )
  const returnPolicyOptions = useMemo(
    () => (['prev', 'any', 'node'] as WfReturnPolicy[]).map((value) => ({ label: t(`workflow.returnPolicy.${value}`), value })),
    [t],
  )
  const onRejectOptions = useMemo(
    () => (['terminate', 'toNode'] as WfRejectAction[]).map((value) => ({ label: t(`workflow.onReject.${value}`), value })),
    [t],
  )
  const timeoutActionOptions = useMemo(
    () => (['remind', 'autoPass', 'autoReject', 'transfer'] as WfTimeoutAction[])
      .map((value) => ({ label: t(`workflow.timeoutAction.${value}`), value })),
    [t],
  )
  const webhookFailureOptions = useMemo(
    () => (['fail', 'manual'] as WfWebhookFailureAction[])
      .map((value) => ({ label: t(`workflow.webhook.failure.${value}`), value })),
    [t],
  )
  const formPermissionOptions = useMemo(
    () => (['editable', 'readonly', 'hidden'] as WfFormPermAccess[])
      .map((value) => ({ label: t(`workflow.form.permission.${value}`), value })),
    [t],
  )
  const webhookMethodOptions = WF_WEBHOOK_METHODS.map((value) => ({ label: value, value }))

  const jumpNodeOptions = useMemo(() => {
    if (!node) return []
    return flattenChain(model.root)
      .filter((candidate) => candidate.id !== node.id)
      .map((candidate) => ({
        label: candidate.name?.trim() || t(`workflow.node.${candidate.type}`),
        value: candidate.id,
      }))
  }, [model, node, t])

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
  const err = (key: string) => (errors[key] ? { validateStatus: 'error' as const, help: t(errors[key]!) } : {})

  const approvalMode: WfApprovalMode = form.provider === 'multiLeader' ? 'seq' : form.mode
  const setApprovalMode = (value: WfApprovalMode) =>
    set(value === 'seq' ? { mode: value, allPassRatio: 100 } : { mode: value })

  const formPermissionFields = formPermissionRows(node, form)

  const showAdvanced = node.type === 'start' || node.type === 'approval' || node.type === 'webhook'
    ? true
    : node.type === 'branch' ? false : form.provider === 'position'

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

  const updateHeader = (index: number, patch: Partial<{ key: string; value: string }>) =>
    // 不可变更新:直接改数组元素不会触发重渲染,输入框会「打不进字」。
    set({ webhookHeaders: form.webhookHeaders.map((header, i) => (i === index ? { ...header, ...patch } : header)) })

  const advanced = (
    <>
      {node.type === 'start' ? (
        <>
          <Form.Item label={t('workflow.designer.initiatorRoles')}>
            <ApiSelect
              mode="multiple" allowClear fetch={fetchRoles}
              value={form.initiatorRoleIds}
              onChange={(value: WfId[]) => set({ initiatorRoleIds: value ?? [] })}
            />
          </Form.Item>
          <Form.Item label={t('workflow.designer.initiatorOrgs')} extra={t('workflow.designer.initiatorScopeHint')}>
            <OrgTreeSelect
              multiple treeCheckable allowClear
              value={form.initiatorOrgIds}
              onChange={(value: WfId[]) => set({ initiatorOrgIds: value ?? [] })}
            />
          </Form.Item>
          <Form.Item label={t('workflow.form.mode')}>
            <Radio.Group
              optionType="button"
              value={form.formMode}
              onChange={(e) => set({ formMode: e.target.value as WfFormMode })}
              options={[
                { value: 'none', label: t('workflow.form.modeNone') },
                { value: 'schema', label: t('workflow.form.modeSchema') },
                { value: 'component', label: t('workflow.form.modeComponent') },
              ]}
            />
          </Form.Item>
          {form.formMode === 'schema' ? (
            <WfFormDesigner value={form.formSchema} onChange={(formSchema) => set({ formSchema })} />
          ) : form.formMode === 'component' ? (
            <Form.Item label={t('workflow.designer.formComponent')} extra={t('workflow.designer.formComponentHint')}>
              <Input
                value={form.formComponent} maxLength={256} placeholder="views/biz/leave/form"
                onChange={(e) => set({ formComponent: e.target.value })}
              />
            </Form.Item>
          ) : null}
        </>
      ) : form.provider === 'position' ? (
        <Form.Item label={t('workflow.designer.positionOrg')}>
          <OrgTreeSelect
            allowClear
            value={form.positionOrgId ?? undefined}
            onChange={(value: WfId | null) => set({ positionOrgId: value ?? null })}
          />
        </Form.Item>
      ) : null}

      {node.type === 'approval' ? (
        <>
          {formPermissionFields.length ? (
            <div className="wf-form-permissions">
              {formPermissionFields.map(({ field, access }) => (
                <Form.Item key={field.key} label={field.label || field.key}>
                  <Select
                    value={access}
                    options={formPermissionOptions}
                    onChange={(value: WfFormPermAccess) => set({ formPerms: { ...form.formPerms, [field.key]: value } })}
                  />
                </Form.Item>
              ))}
            </div>
          ) : null}
          <Form.Item label={t('workflow.designer.returnPolicy')}>
            <Select
              value={form.returnPolicy} options={returnPolicyOptions}
              onChange={(returnPolicy: WfReturnPolicy) => set({ returnPolicy })}
            />
          </Form.Item>
          {form.returnPolicy === 'node' ? (
            <Form.Item label={t('workflow.designer.returnToNode')}>
              <Select
                allowClear value={form.returnToNodeId ?? undefined} options={jumpNodeOptions}
                onChange={(returnToNodeId?: string) => set({ returnToNodeId: returnToNodeId ?? null })}
              />
            </Form.Item>
          ) : null}
          <Form.Item label={t('workflow.designer.onReject')}>
            <Select
              value={form.onReject} options={onRejectOptions}
              onChange={(value: WfRejectAction) => set({ onReject: value })}
            />
          </Form.Item>
          {form.onReject === 'toNode' ? (
            <Form.Item label={t('workflow.designer.rejectToNode')}>
              <Select
                allowClear value={form.rejectToNodeId ?? undefined} options={jumpNodeOptions}
                onChange={(rejectToNodeId?: string) => set({ rejectToNodeId: rejectToNodeId ?? null })}
              />
            </Form.Item>
          ) : null}
          <Form.Item label={t('workflow.designer.timeoutHours')}>
            <InputNumber
              className="w-full" min={0} max={8760} value={form.timeoutHours}
              onChange={(value) => set({ timeoutHours: value ?? 0 })}
            />
          </Form.Item>
          {form.timeoutHours > 0 ? (
            <Form.Item label={t('workflow.designer.timeoutAction')}>
              <Select
                value={form.timeoutAction} options={timeoutActionOptions}
                onChange={(timeoutAction: WfTimeoutAction) => set({ timeoutAction })}
              />
            </Form.Item>
          ) : null}
          {form.timeoutHours > 0 && form.timeoutAction === 'transfer' ? (
            <Form.Item label={t('workflow.designer.timeoutTransfer')}>
              <UserSelect
                allowClear
                value={form.timeoutTransferUserId ?? undefined}
                onChange={(value: WfId | null) => set({ timeoutTransferUserId: value ?? null })}
              />
            </Form.Item>
          ) : null}
          <Form.Item label={t('workflow.designer.labelApprove')}>
            <Input value={form.labelApprove} placeholder={t('workflow.detail.approve')} onChange={(e) => set({ labelApprove: e.target.value })} />
          </Form.Item>
          <Form.Item label={t('workflow.designer.labelReject')}>
            <Input value={form.labelReject} placeholder={t('workflow.detail.reject')} onChange={(e) => set({ labelReject: e.target.value })} />
          </Form.Item>
          <Form.Item label={t('workflow.designer.labelReturn')}>
            <Input value={form.labelReturn} placeholder={t('workflow.detail.return')} onChange={(e) => set({ labelReturn: e.target.value })} />
          </Form.Item>
          <Form.Item label={t('workflow.designer.labelTransfer')}>
            <Input value={form.labelTransfer} placeholder={t('workflow.detail.transfer')} onChange={(e) => set({ labelTransfer: e.target.value })} />
          </Form.Item>
          <Form.Item label={t('workflow.designer.labelDelegate')}>
            <Input value={form.labelDelegate} placeholder={t('workflow.detail.delegate')} onChange={(e) => set({ labelDelegate: e.target.value })} />
          </Form.Item>
          <Form.Item label={t('workflow.designer.labelUrge')}>
            <Input value={form.labelUrge} placeholder={t('workflow.detail.urge')} onChange={(e) => set({ labelUrge: e.target.value })} />
          </Form.Item>
        </>
      ) : node.type === 'webhook' ? (
        <>
          <Form.Item label={t('workflow.webhook.headers')}>
            <div className="wf-webhook-headers">
              {form.webhookHeaders.map((header, index) => (
                <div key={index} className="wf-webhook-header">
                  <Input
                    value={header.key} placeholder={t('workflow.webhook.headerName')}
                    onChange={(e) => updateHeader(index, { key: e.target.value })}
                  />
                  <Input
                    value={header.value} placeholder={t('workflow.webhook.headerValue')}
                    onChange={(e) => updateHeader(index, { value: e.target.value })}
                  />
                  <Button
                    type="text" shape="circle"
                    aria-label={t('workflow.webhook.removeHeader')} title={t('workflow.webhook.removeHeader')}
                    icon={<AppIcon icon="ph:x" size={14} />}
                    onClick={() => set({ webhookHeaders: form.webhookHeaders.filter((_, i) => i !== index) })}
                  />
                </div>
              ))}
              <Button
                type="dashed" block icon={<AppIcon icon="ph:plus" size={14} />}
                onClick={() => set({ webhookHeaders: [...form.webhookHeaders, { key: '', value: '' }] })}
              >
                {t('workflow.webhook.addHeader')}
              </Button>
            </div>
          </Form.Item>
          <Form.Item label={t('workflow.webhook.maxAttempts')} {...err('maxAttempts')}>
            <InputNumber
              className="w-full" min={1} max={100} precision={0} value={form.maxAttempts}
              onChange={(value) => set({ maxAttempts: value ?? 1 })}
            />
          </Form.Item>
        </>
      ) : null}
    </>
  )

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

        {node.type === 'start' ? (
          <Form.Item label={t('workflow.designer.initiatorUsers')}>
            <UserSelect
              mode="multiple" allowClear
              value={form.initiatorUserIds}
              onChange={(value: WfId[]) => set({ initiatorUserIds: value ?? [] })}
            />
          </Form.Item>
        ) : node.type === 'branch' ? (
          <Collapse
            accordion
            ghost
            className="wf-branch-conditions"
            defaultActiveKey={(node.conditions ?? []).find((arm) => !arm.isDefault)?.id}
            items={(node.conditions ?? []).map((arm) => ({
              key: arm.id,
              label: <span className="wf-branch-condition-title">{arm.name || t('workflow.designer.armName')}</span>,
              children: arm.isDefault ? (
                <div className="wf-branch-default-hint">{t('workflow.condition.defaultHint')}</div>
              ) : (
                <WfConditionEditor
                  value={form.armExpressions[arm.id] ?? createConditionGroup()}
                  onChange={(expr) => set({ armExpressions: { ...form.armExpressions, [arm.id]: expr } })}
                />
              ),
            }))}
          />
        ) : node.type === 'parallel' ? (
          <p className="wf-parallel-hint">{t('workflow.designer.parallelHint')}</p>
        ) : node.type === 'webhook' ? (
          <>
            <Form.Item label={t('workflow.webhook.url')} {...err('webhookUrl')}>
              <Input
                value={form.webhookUrl}
                aria-label={t('workflow.webhook.url')}
                placeholder={t('workflow.webhook.urlPlaceholder')}
                onChange={(e) => set({ webhookUrl: e.target.value })}
              />
            </Form.Item>
            <Form.Item label={t('workflow.webhook.method')}>
              <Select
                value={form.webhookMethod} options={webhookMethodOptions}
                onChange={(webhookMethod: WfWebhookMethod) => set({ webhookMethod })}
              />
            </Form.Item>
            <Form.Item label={t('workflow.webhook.timeoutSeconds')} {...err('webhookTimeoutSeconds')}>
              <InputNumber
                className="w-full" min={1} max={120} precision={0} value={form.webhookTimeoutSeconds}
                onChange={(value) => set({ webhookTimeoutSeconds: value ?? 1 })}
              />
            </Form.Item>
            <Form.Item label={t('workflow.webhook.onFailure')}>
              <Select
                value={form.webhookOnFailure} options={webhookFailureOptions}
                onChange={(webhookOnFailure: WfWebhookFailureAction) => set({ webhookOnFailure })}
              />
            </Form.Item>
          </>
        ) : (
          <>
            <Form.Item label={t('workflow.designer.assignee')}>
              <Select value={form.provider} options={providerOptions} onChange={(provider: string) => set({ provider })} />
            </Form.Item>
            {form.provider === 'user' ? (
              <Form.Item label={t('workflow.designer.users')}>
                <UserSelect
                  mode="multiple" placeholder={t('workflow.designer.users')}
                  value={form.userIds}
                  onChange={(value: WfId[]) => set({ userIds: value ?? [] })}
                />
              </Form.Item>
            ) : form.provider === 'leader' || form.provider === 'multiLeader' ? (
              <Form.Item label={t('workflow.designer.level')}>
                <InputNumber className="w-full" min={1} max={20} value={form.level} onChange={(value) => set({ level: value ?? 1 })} />
              </Form.Item>
            ) : form.provider === 'role' ? (
              <Form.Item label={t('workflow.designer.role')}>
                <ApiSelect
                  fetch={fetchRoles} placeholder={t('workflow.designer.role')}
                  value={form.roleId ?? undefined}
                  onChange={(value: WfId | null) => set({ roleId: value ?? null })}
                />
              </Form.Item>
            ) : form.provider === 'position' ? (
              <Form.Item label={t('workflow.designer.position')}>
                <ApiSelect
                  fetch={fetchPositions} placeholder={t('workflow.designer.position')}
                  value={form.positionId ?? undefined}
                  onChange={(value: WfId | null) => set({ positionId: value ?? null })}
                />
              </Form.Item>
            ) : null}

            {node.type === 'approval' ? (
              <Form.Item label={t('workflow.designer.mode')}>
                <Select
                  value={approvalMode} options={modeOptions} disabled={form.provider === 'multiLeader'}
                  onChange={setApprovalMode}
                />
              </Form.Item>
            ) : null}
            {node.type === 'approval' && approvalMode === 'all' ? (
              <Form.Item label={t('workflow.designer.allPassRatio')} {...err('allPassRatio')}>
                <InputNumber
                  className="w-full" min={1} max={100} precision={0} value={form.allPassRatio}
                  onChange={(value) => set({ allPassRatio: value ?? 100 })}
                />
              </Form.Item>
            ) : null}
          </>
        )}

        {showAdvanced ? (
          <Collapse
            ghost
            className="wf-advanced"
            items={[{
              key: 'advanced',
              label: <span className="wf-advanced-title">{t('workflow.designer.advanced')}</span>,
              children: advanced,
            }]}
          />
        ) : null}
      </Form>
    </FormContainer>
  )
}
