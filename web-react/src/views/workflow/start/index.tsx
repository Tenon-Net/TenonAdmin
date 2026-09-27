// 发起流程页。菜单 component 填 `workflow/start/index`;可选 `?definitionId=` 预选已发布定义。
// 业务表单走 WfFormMount:定义有内置 formSchema 就渲染运行时表单,否则挂消费者 formComponent。
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { App, Button, Card, Form, Input, Select, Space } from 'antd'
import { useTranslation } from 'react-i18next'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { AppIcon } from '@/components/AppIcon'
import { UserSelect } from '@/components/UserSelect'
import { wfInstanceApi } from '@/api/workflow'
import { translateError } from '@/utils/error'
import { projectWfRuntimeModel } from '@/workflow/formSchema'
import { normalizeWfId, wfIdEquals, type WfId } from '@/workflow/id'
import { flattenChain } from '@/workflow/model'
import { classifyOutcome, useRequestKey } from '@/workflow/useRequestKey'
import type { WfStartableDefinitionDetail } from '@/types/workflow'
import { WfFormMount, type WfFormMountHandle } from '../components/WfFormMount'
import { serializeVars, type WfVarRow } from './startForm'

interface StartFormValues {
  definitionId?: WfId
  businessKey?: string
  varRows?: WfVarRow[]
  selectedUserIdsByNode?: Record<string, WfId[] | undefined>
}

export default function WfStartPage() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()

  // useRequestKey 是工厂(键存在闭包里)不是 hook:useRef 固定住首个实例,否则每次渲染都换一把新键。
  const requestKey = useRef(useRequestKey()).current

  const [form] = Form.useForm<StartFormValues>()
  const [defOptions, setDefOptions] = useState<{ label: string; value: WfId }[]>([])
  const [defsLoading, setDefsLoading] = useState(false)
  const [snapshot, setSnapshot] = useState<WfStartableDefinitionDetail | null>(null)
  const [snapshotLoading, setSnapshotLoading] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [runtimeVariablesJson, setRuntimeVariablesJson] = useState<string | null>(null)
  const formRuntimeRef = useRef<WfFormMountHandle | null>(null)
  const definitionSeqRef = useRef(0)

  const loadDefinition = useCallback(
    async (id: WfId | undefined) => {
      const seq = ++definitionSeqRef.current
      setSnapshot(null)
      setSnapshotLoading(!!id)
      setRuntimeVariablesJson(null)
      form.setFieldsValue({ varRows: [{}], selectedUserIdsByNode: {} })
      if (!id) return
      try {
        const loaded = await wfInstanceApi.startableDetail(id)
        if (seq === definitionSeqRef.current) setSnapshot(loaded)
      } catch (e) {
        if (seq === definitionSeqRef.current) message.error(translateError(e))
      } finally {
        if (seq === definitionSeqRef.current) setSnapshotLoading(false)
      }
    },
    [form, message],
  )

  useEffect(() => {
    setDefsLoading(true)
    wfInstanceApi
      .startable()
      .then((defs) => {
        setDefOptions(
          defs
            .map((d) => ({ label: d.name ?? '', value: normalizeWfId(d.id) }))
            .filter((o): o is { label: string; value: WfId } => o.value !== null),
        )
        // ?definitionId= 预选:定义列表到位后再落值,免得 Select 显示一个还没有选项的 id。
        const preset = normalizeWfId(searchParams.get('definitionId'))
        if (preset !== null) {
          form.setFieldValue('definitionId', preset)
          void loadDefinition(preset)
        }
      })
      .catch((e: unknown) => message.error(translateError(e)))
      .finally(() => setDefsLoading(false))
  }, [form, loadDefinition, message, searchParams])

  const runtimeModel = useMemo(() => projectWfRuntimeModel(snapshot?.model), [snapshot])
  const formComponent = snapshot?.model?.formComponent ?? snapshot?.formComponent ?? null
  const formSchema = runtimeModel?.formSchema ?? null
  const hasBuiltinForm = !!formSchema?.fields.length
  const hasFormMount = !!formComponent?.trim()

  // 无内置表单时,摘要变量也要喂给消费者挂载点(与 Vue 一致:它把键值行序列化后一起传下去)。
  const varRows = Form.useWatch('varRows', form)
  const kvVariablesJson = useMemo(() => serializeVars(varRows ?? []), [varRows])
  // getFieldValue 在渲染期读不到后续变化,业务键要跟着输入走就得 useWatch 订阅。
  const businessKey = Form.useWatch('businessKey', form)
  const selectedDefinitionId = Form.useWatch('definitionId', form)
  const snapshotMatchesSelection =
    !!selectedDefinitionId && wfIdEquals(snapshot?.id, selectedDefinitionId)

  /** 发起人自选审批人的节点:每个节点一个多选人员框,提交时按 nodeId 归拢。 */
  const selfSelectNodes = useMemo(
    () => (runtimeModel?.root ? flattenChain(runtimeModel.root).filter((n) => n.props?.assignee?.provider === 'selfSelect') : []),
    [runtimeModel],
  )

  const submit = async () => {
    const v = await form.validateFields().catch(() => null)
    if (!v?.definitionId) return
    if (!snapshotMatchesSelection || !wfIdEquals(v.definitionId, selectedDefinitionId)) return
    if (hasBuiltinForm && formRuntimeRef.current?.validate() === false) return

    setSubmitting(true)
    try {
      const picked = selfSelectNodes
        .map((node) => [node.id, v.selectedUserIdsByNode?.[node.id] ?? []] as const)
        .filter(([, ids]) => ids.length > 0)
      const result = await wfInstanceApi.start({
        definitionId: v.definitionId,
        businessKey: v.businessKey?.trim() || null,
        variablesJson: hasBuiltinForm ? runtimeVariablesJson : serializeVars(v.varRows ?? []),
        selectedUserIdsByNode: Object.fromEntries(picked),
        requestId: requestKey.value(),
      })
      requestKey.settle('success')
      message.success(t('workflow.start.started'))
      navigate(`/workflow/instance/${result.instanceId}/detail`)
    } catch (e) {
      requestKey.settle(classifyOutcome(e))
      message.error(translateError(e))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Card title={t('workflow.start.title')} size="small">
      <Form
        form={form}
        labelCol={{ span: 5 }}
        wrapperCol={{ span: 19 }}
        style={{ maxWidth: 720 }}
        initialValues={{ varRows: [{}] }}
        onValuesChange={() => requestKey.reset()}
      >
        <Form.Item
          name="definitionId" label={t('workflow.start.definition')}
          rules={[{ required: true, message: t('workflow.start.definitionRequired') }]}
        >
          <Select
            options={defOptions}
            loading={defsLoading}
            allowClear
            showSearch={{ optionFilterProp: 'label' }}
            placeholder={t('workflow.start.definitionPlaceholder')}
            onChange={(id: WfId | undefined) => void loadDefinition(id)}
          />
        </Form.Item>

        <Form.Item name="businessKey" label={t('workflow.start.businessKey')}>
          <Input placeholder={t('workflow.start.businessKeyHint')} />
        </Form.Item>

        {hasBuiltinForm ? null : (
          <Form.Item label={t('workflow.start.variables')} extra={t('workflow.start.variablesHint')}>
            <Form.List name="varRows">
              {(fields, { add, remove }) => (
                <Space orientation="vertical" size={8} style={{ width: '100%' }}>
                  {fields.map((field) => (
                    <Space key={field.key} size={8} style={{ width: '100%' }}>
                      <Form.Item name={[field.name, 'key']} noStyle>
                        <Input placeholder={t('workflow.start.varKey')} style={{ width: 180 }} />
                      </Form.Item>
                      <Form.Item name={[field.name, 'value']} noStyle>
                        <Input placeholder={t('workflow.start.varValue')} style={{ width: 240 }} />
                      </Form.Item>
                      <Button
                        type="text" size="small" aria-label={t('common.delete')}
                        icon={<AppIcon icon="ph:x" size={14} />}
                        // 删到空就补一行空行:一行都不留时用户没有可点的输入格。
                        onClick={() => { remove(field.name); if (fields.length === 1) add({}) }}
                      />
                    </Space>
                  ))}
                  <Button type="dashed" size="small" icon={<AppIcon icon="ph:plus" size={14} />} onClick={() => add({})}>
                    {t('workflow.start.addVariable')}
                  </Button>
                </Space>
              )}
            </Form.List>
          </Form.Item>
        )}

        {selfSelectNodes.map((node) => (
          <Form.Item
            key={node.id}
            name={['selectedUserIdsByNode', node.id]}
            label={node.name || t('workflow.start.selfSelect')}
          >
            <UserSelect mode="multiple" placeholder={t('workflow.start.selfSelectHint')} />
          </Form.Item>
        ))}

      </Form>

      {hasFormMount || hasBuiltinForm ? (
        <div style={{ marginBlock: 12 }}>
          <WfFormMount
            ref={formRuntimeRef}
            formComponent={formComponent}
            formSchema={formSchema}
            mode="start"
            definitionId={snapshot?.id}
            businessKey={businessKey || null}
            variablesJson={hasBuiltinForm ? runtimeVariablesJson : kvVariablesJson}
            onVariablesChange={(next) => {
              requestKey.reset()
              setRuntimeVariablesJson(next)
            }}
          />
        </div>
      ) : null}

      <Space style={{ marginTop: 16 }}>
        <Button
          type="primary"
          loading={submitting}
          disabled={snapshotLoading || !snapshotMatchesSelection}
          onClick={() => void submit()}
        >
          {t('workflow.start.submit')}
        </Button>
      </Space>
    </Card>
  )
}
