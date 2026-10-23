import { useMemo, type ReactNode } from 'react'
import { Button, Collapse, Form, Input, InputNumber, Radio, Select } from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { ApiSelect, type ApiFetch } from '@/components/ApiSelect'
import { OrgTreeSelect } from '@/components/OrgTreeSelect'
import { UserSelect } from '@/components/UserSelect'
import { positionApi, roleApi } from '@/api'
import { createConditionGroup, WF_WEBHOOK_METHODS, type WfWebhookMethod } from '@/workflow/configuration'
import { flattenChain } from '@/workflow/model'
import type {
  WfApprovalMode,
  WfAssigneeProvider,
  WfFormPermAccess,
  WfModel,
  WfNode,
  WfRejectAction,
  WfReturnPolicy,
  WfTimeoutAction,
  WfWebhookFailureAction,
} from '@/workflow/schema'
import type { WfId } from '@/workflow/id'
import type { WfConfigDrawerForm } from './WfConfigForm'
import { WfConditionEditor } from './WfConditionEditor'
import { WfFormDesigner } from './WfFormDesigner'

type SetForm = (patch: Partial<WfConfigDrawerForm>) => void

interface FieldsProps {
  node: WfNode
  model: WfModel
  form: WfConfigDrawerForm
  set: SetForm
  errors: Record<string, string>
}

type FormFieldsProps = Pick<FieldsProps, 'form' | 'set'>
type ValidationFieldsProps = Pick<FieldsProps, 'form' | 'set' | 'errors'>
type ApprovalFieldsProps = Pick<FieldsProps, 'node' | 'model' | 'form' | 'set' | 'errors'>

const fetchRoles: ApiFetch = async (keyword) => {
  const { items } = await roleApi.page({ page: 1, pageSize: 50, name: keyword || undefined })
  return items.map((role) => ({ label: role.name, value: role.id }))
}

const fetchPositions: ApiFetch = async (keyword) => {
  const { items } = await positionApi.page({ page: 1, pageSize: 50, name: keyword || undefined })
  return items.map((position) => ({ label: position.name, value: position.id }))
}

function Advanced({ children }: { children: ReactNode }) {
  const { t } = useTranslation()
  return (
    <Collapse
      ghost
      className="wf-advanced"
      items={[{
        key: 'advanced',
        label: <span className="wf-advanced-title">{t('workflow.designer.advanced')}</span>,
        children,
      }]}
    />
  )
}

function StartConfigFields({ form, set }: FormFieldsProps) {
  const { t } = useTranslation()
  return (
    <>
      <Form.Item label={t('workflow.designer.initiatorUsers')}>
        <UserSelect
          source="workflow" mode="multiple" allowClear value={form.initiatorUserIds}
          onChange={(value: WfId[]) => set({ initiatorUserIds: value ?? [] })}
        />
      </Form.Item>
      <Advanced>
        <Form.Item label={t('workflow.designer.initiatorRoles')}>
          <ApiSelect
            mode="multiple" allowClear fetch={fetchRoles} value={form.initiatorRoleIds}
            onChange={(value: WfId[]) => set({ initiatorRoleIds: value ?? [] })}
          />
        </Form.Item>
        <Form.Item label={t('workflow.designer.initiatorOrgs')} extra={t('workflow.designer.initiatorScopeHint')}>
          <OrgTreeSelect
            multiple treeCheckable allowClear value={form.initiatorOrgIds}
            onChange={(value: WfId[]) => set({ initiatorOrgIds: value ?? [] })}
          />
        </Form.Item>
        <Form.Item label={t('workflow.form.mode')}>
          <Radio.Group
            optionType="button" value={form.formMode}
            onChange={(event) => set({ formMode: event.target.value as WfConfigDrawerForm['formMode'] })}
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
              onChange={(event) => set({ formComponent: event.target.value })}
            />
          </Form.Item>
        ) : null}
      </Advanced>
    </>
  )
}

function PositionOrgField({ form, set }: FormFieldsProps) {
  const { t } = useTranslation()
  if (form.provider !== 'position') return null
  return (
    <Form.Item label={t('workflow.designer.positionOrg')}>
      <OrgTreeSelect
        allowClear value={form.positionOrgId ?? undefined}
        onChange={(value: WfId | null) => set({ positionOrgId: value ?? null })}
      />
    </Form.Item>
  )
}

function AssigneeFields({ form, set }: FormFieldsProps) {
  const { t } = useTranslation()
  const providerOptions = useMemo(
    () => (['user', 'leader', 'multiLeader', 'role', 'position', 'selfSelect', 'initiator', 'orgLeader'] as WfAssigneeProvider[])
      .map((value) => ({ label: t(`workflow.provider.${value}`), value })),
    [t],
  )
  return (
    <>
      <Form.Item label={t('workflow.designer.assignee')}>
        <Select value={form.provider} options={providerOptions} onChange={(provider: string) => set({ provider })} />
      </Form.Item>
      {form.provider === 'user' ? (
        <Form.Item label={t('workflow.designer.users')}>
          <UserSelect
            source="workflow" mode="multiple" placeholder={t('workflow.designer.users')} value={form.userIds}
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
            fetch={fetchRoles} placeholder={t('workflow.designer.role')} value={form.roleId ?? undefined}
            onChange={(value: WfId | null) => set({ roleId: value ?? null })}
          />
        </Form.Item>
      ) : form.provider === 'position' ? (
        <Form.Item label={t('workflow.designer.position')}>
          <ApiSelect
            fetch={fetchPositions} placeholder={t('workflow.designer.position')} value={form.positionId ?? undefined}
            onChange={(value: WfId | null) => set({ positionId: value ?? null })}
          />
        </Form.Item>
      ) : null}
    </>
  )
}

export function formPermissionRows(node: WfNode, form: WfConfigDrawerForm) {
  if (node.type !== 'approval' || form.formComponent.trim() || !form.formSchema?.fields.length) return []
  return form.formSchema.fields.map((field) => ({
    field,
    access: form.formPerms[field.key] ?? ('editable' as WfFormPermAccess),
  }))
}

function ApprovalAdvancedFields({ node, model, form, set, errors }: ApprovalFieldsProps) {
  const { t } = useTranslation()
  const err = (key: string) => errors[key] ? { validateStatus: 'error' as const, help: t(errors[key]) } : {}
  const approvalMode: WfApprovalMode = form.provider === 'multiLeader' ? 'seq' : form.mode
  const modeOptions = (['any', 'all', 'seq'] as WfApprovalMode[]).map((value) => ({ label: t(`workflow.mode.${value}`), value }))
  const returnPolicyOptions = (['prev', 'any', 'node'] as WfReturnPolicy[]).map((value) => ({ label: t(`workflow.returnPolicy.${value}`), value }))
  const rejectOptions = (['terminate', 'toNode'] as WfRejectAction[]).map((value) => ({ label: t(`workflow.onReject.${value}`), value }))
  const timeoutOptions = (['remind', 'autoPass', 'autoReject', 'transfer'] as WfTimeoutAction[]).map((value) => ({ label: t(`workflow.timeoutAction.${value}`), value }))
  const permissionOptions = (['editable', 'readonly', 'hidden'] as WfFormPermAccess[]).map((value) => ({ label: t(`workflow.form.permission.${value}`), value }))
  const jumpNodeOptions = flattenChain(model.root)
    .filter((candidate) => candidate.id !== node.id)
    .map((candidate) => ({ label: candidate.name?.trim() || t(`workflow.node.${candidate.type}`), value: candidate.id }))
  const permissionFields = formPermissionRows(node, form)
  const setMode = (mode: WfApprovalMode) => set(mode === 'seq' ? { mode, allPassRatio: 100 } : { mode })

  return (
    <>
      <Form.Item label={t('workflow.designer.mode')}>
        <Select value={approvalMode} options={modeOptions} disabled={form.provider === 'multiLeader'} onChange={setMode} />
      </Form.Item>
      {approvalMode === 'all' ? (
        <Form.Item label={t('workflow.designer.allPassRatio')} {...err('allPassRatio')}>
          <InputNumber className="w-full" min={1} max={100} precision={0} value={form.allPassRatio} onChange={(value) => set({ allPassRatio: value ?? 100 })} />
        </Form.Item>
      ) : null}
      <Advanced>
        <PositionOrgField form={form} set={set} />
        {permissionFields.length ? (
          <div className="wf-form-permissions">
            {permissionFields.map(({ field, access }) => (
              <Form.Item key={field.key} label={field.label || field.key}>
                <Select value={access} options={permissionOptions} onChange={(value: WfFormPermAccess) => set({ formPerms: { ...form.formPerms, [field.key]: value } })} />
              </Form.Item>
            ))}
          </div>
        ) : null}
        <Form.Item label={t('workflow.designer.returnPolicy')}>
          <Select value={form.returnPolicy} options={returnPolicyOptions} onChange={(returnPolicy: WfReturnPolicy) => set({ returnPolicy })} />
        </Form.Item>
        {form.returnPolicy === 'node' ? (
          <Form.Item label={t('workflow.designer.returnToNode')}>
            <Select allowClear value={form.returnToNodeId ?? undefined} options={jumpNodeOptions} onChange={(value?: string) => set({ returnToNodeId: value ?? null })} />
          </Form.Item>
        ) : null}
        <Form.Item label={t('workflow.designer.onReject')}>
          <Select value={form.onReject} options={rejectOptions} onChange={(onReject: WfRejectAction) => set({ onReject })} />
        </Form.Item>
        {form.onReject === 'toNode' ? (
          <Form.Item label={t('workflow.designer.rejectToNode')}>
            <Select allowClear value={form.rejectToNodeId ?? undefined} options={jumpNodeOptions} onChange={(value?: string) => set({ rejectToNodeId: value ?? null })} />
          </Form.Item>
        ) : null}
        <Form.Item label={t('workflow.designer.timeoutHours')}>
          <InputNumber className="w-full" min={0} max={8760} value={form.timeoutHours} onChange={(value) => set({ timeoutHours: value ?? 0 })} />
        </Form.Item>
        {form.timeoutHours > 0 ? (
          <Form.Item label={t('workflow.designer.timeoutAction')}>
            <Select value={form.timeoutAction} options={timeoutOptions} onChange={(timeoutAction: WfTimeoutAction) => set({ timeoutAction })} />
          </Form.Item>
        ) : null}
        {form.timeoutHours > 0 && form.timeoutAction === 'transfer' ? (
          <Form.Item label={t('workflow.designer.timeoutTransfer')}>
            <UserSelect source="workflow" allowClear value={form.timeoutTransferUserId ?? undefined} onChange={(value: WfId | null) => set({ timeoutTransferUserId: value ?? null })} />
          </Form.Item>
        ) : null}
        {(['Approve', 'Reject', 'Return', 'Transfer', 'Delegate', 'Urge'] as const).map((suffix) => {
          const key = `label${suffix}` as keyof Pick<WfConfigDrawerForm, 'labelApprove' | 'labelReject' | 'labelReturn' | 'labelTransfer' | 'labelDelegate' | 'labelUrge'>
          const action = suffix.toLowerCase()
          return (
            <Form.Item key={key} label={t(`workflow.designer.${key}`)}>
              <Input value={form[key]} placeholder={t(`workflow.detail.${action}`)} onChange={(event) => set({ [key]: event.target.value })} />
            </Form.Item>
          )
        })}
      </Advanced>
    </>
  )
}

function WebhookConfigFields({ form, set, errors }: ValidationFieldsProps) {
  const { t } = useTranslation()
  const err = (key: string) => errors[key] ? { validateStatus: 'error' as const, help: t(errors[key]) } : {}
  const failureOptions = (['fail', 'manual'] as WfWebhookFailureAction[]).map((value) => ({ label: t(`workflow.webhook.failure.${value}`), value }))
  const updateHeader = (index: number, patch: Partial<{ key: string; value: string }>) =>
    set({ webhookHeaders: form.webhookHeaders.map((header, current) => current === index ? { ...header, ...patch } : header) })
  return (
    <>
      <Form.Item label={t('workflow.webhook.url')} {...err('webhookUrl')}>
        <Input value={form.webhookUrl} aria-label={t('workflow.webhook.url')} placeholder={t('workflow.webhook.urlPlaceholder')} onChange={(event) => set({ webhookUrl: event.target.value })} />
      </Form.Item>
      <Form.Item label={t('workflow.webhook.method')}>
        <Select value={form.webhookMethod} options={WF_WEBHOOK_METHODS.map((value) => ({ label: value, value }))} onChange={(webhookMethod: WfWebhookMethod) => set({ webhookMethod })} />
      </Form.Item>
      <Form.Item label={t('workflow.webhook.timeoutSeconds')} {...err('webhookTimeoutSeconds')}>
        <InputNumber className="w-full" min={1} max={120} precision={0} value={form.webhookTimeoutSeconds} onChange={(value) => set({ webhookTimeoutSeconds: value ?? 1 })} />
      </Form.Item>
      <Form.Item label={t('workflow.webhook.onFailure')}>
        <Select value={form.webhookOnFailure} options={failureOptions} onChange={(webhookOnFailure: WfWebhookFailureAction) => set({ webhookOnFailure })} />
      </Form.Item>
      <Advanced>
        <Form.Item label={t('workflow.webhook.headers')}>
          <div className="wf-webhook-headers">
            {form.webhookHeaders.map((header, index) => (
              <div key={index} className="wf-webhook-header">
                <Input value={header.key} placeholder={t('workflow.webhook.headerName')} onChange={(event) => updateHeader(index, { key: event.target.value })} />
                <Input value={header.value} placeholder={t('workflow.webhook.headerValue')} onChange={(event) => updateHeader(index, { value: event.target.value })} />
                <Button
                  type="text" shape="circle" aria-label={t('workflow.webhook.removeHeader')} title={t('workflow.webhook.removeHeader')}
                  icon={<AppIcon icon="ph:x" size={14} />}
                  onClick={() => set({ webhookHeaders: form.webhookHeaders.filter((_, current) => current !== index) })}
                />
              </div>
            ))}
            <Button type="dashed" block icon={<AppIcon icon="ph:plus" size={14} />} onClick={() => set({ webhookHeaders: [...form.webhookHeaders, { key: '', value: '' }] })}>
              {t('workflow.webhook.addHeader')}
            </Button>
          </div>
        </Form.Item>
        <Form.Item label={t('workflow.webhook.maxAttempts')} {...err('maxAttempts')}>
          <InputNumber className="w-full" min={1} max={100} precision={0} value={form.maxAttempts} onChange={(value) => set({ maxAttempts: value ?? 1 })} />
        </Form.Item>
      </Advanced>
    </>
  )
}

export function WfConfigFields(props: FieldsProps) {
  const { t } = useTranslation()
  const { node, form, set } = props
  if (node.type === 'start') return <StartConfigFields {...props} />
  if (node.type === 'branch') {
    return (
      <Collapse
        accordion ghost className="wf-branch-conditions"
        defaultActiveKey={(node.conditions ?? []).find((arm) => !arm.isDefault)?.id}
        items={(node.conditions ?? []).map((arm) => ({
          key: arm.id,
          label: <span className="wf-branch-condition-title">{arm.name || t('workflow.designer.armName')}</span>,
          children: arm.isDefault ? (
            <div className="wf-branch-default-hint">{t('workflow.condition.defaultHint')}</div>
          ) : (
            <WfConditionEditor value={form.armExpressions[arm.id] ?? createConditionGroup()} onChange={(expr) => set({ armExpressions: { ...form.armExpressions, [arm.id]: expr } })} />
          ),
        }))}
      />
    )
  }
  if (node.type === 'parallel') return <p className="wf-parallel-hint">{t('workflow.designer.parallelHint')}</p>
  if (node.type === 'webhook') return <WebhookConfigFields {...props} />
  return (
    <>
      <AssigneeFields {...props} />
      {node.type === 'approval' ? (
        <ApprovalAdvancedFields {...props} />
      ) : form.provider === 'position' ? (
        <Advanced><PositionOrgField form={form} set={set} /></Advanced>
      ) : null}
    </>
  )
}
