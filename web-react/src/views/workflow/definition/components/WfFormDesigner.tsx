// 简易动态表单设计器:单列 10 控件,增删/上下移/通用属性/类型专属属性 + 即时校验。
// 对应 Vue 侧 WfFormDesigner.vue。不引入拖拽依赖(上下移按钮即排序手段)。
import { useMemo } from 'react'
import { Button, Card, Form, Input, InputNumber, Select, Space, Switch } from 'antd'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import {
  addWfFormField,
  createWfFormField,
  getWfFormFieldProp,
  moveWfFormField,
  removeWfFormField,
  updateWfFormFieldProp,
  validateWfFormSchema,
} from '@/workflow/formSchema'
import {
  WF_FORM_FIELD_TYPES,
  type WfFormField,
  type WfFormFieldType,
  type WfFormOption,
  type WfFormSchema,
} from '@/workflow/schema'
import './wf-designer.css'

const MAX_FIELDS = 50

export interface WfFormDesignerProps {
  value: WfFormSchema | null
  /** 删掉最后一个字段时回传 null(= 该流程无内置表单)。 */
  onChange: (value: WfFormSchema | null) => void
}

export function WfFormDesigner({ value, onChange }: WfFormDesignerProps) {
  const { t } = useTranslation()

  const typeOptions = useMemo(
    () => WF_FORM_FIELD_TYPES.map((type) => ({ value: type, label: t(`workflow.form.type.${type}`) })),
    [t],
  )
  const issues = useMemo(() => validateWfFormSchema(value), [value])
  const schemaErrors = [...new Set(
    issues.filter((issue) => issue.fieldIndex == null).map((issue) => t(`workflow.form.error.${issue.code}`)),
  )]
  const fieldErrors = (index: number) => [...new Set(
    issues.filter((issue) => issue.fieldIndex === index).map((issue) => t(`workflow.form.error.${issue.code}`)),
  )]

  const fields = value?.fields ?? []

  const updateField = (index: number, update: (field: WfFormField) => WfFormField) => {
    const field = fields[index]
    if (!field) return
    const next = [...fields]
    next[index] = update(field)
    onChange({ version: 1, fields: next })
  }

  const updateProp = (index: number, key: string, propValue: unknown) => {
    updateField(index, (current) => updateWfFormFieldProp(current, key, propValue))
  }

  const prop = (field: WfFormField, key: string): unknown => getWfFormFieldProp(field, key)
  const numberProp = (field: WfFormField, key: string): number | null => {
    const raw = prop(field, key)
    return typeof raw === 'number' ? raw : null
  }
  const stringProp = (field: WfFormField, key: string): string => {
    const raw = prop(field, key)
    return typeof raw === 'string' ? raw : ''
  }
  const booleanProp = (field: WfFormField, key: string): boolean => prop(field, key) === true
  const options = (field: WfFormField): WfFormOption[] => {
    const raw = prop(field, 'options')
    return Array.isArray(raw) ? raw : []
  }

  const updateOption = (fieldIndex: number, optionIndex: number, patch: Partial<WfFormOption>) => {
    const field = fields[fieldIndex]
    if (!field) return
    updateProp(fieldIndex, 'options', options(field).map((option, index) =>
      index === optionIndex ? { ...option, ...patch } : option))
  }

  const addOption = (fieldIndex: number) => {
    const field = fields[fieldIndex]
    if (!field) return
    const current = options(field)
    let n = current.length + 1
    while (current.some((option) => option.value === `option${n}`)) n += 1
    updateProp(fieldIndex, 'options', [...current, { label: `${t('workflow.form.option')} ${n}`, value: `option${n}` }])
  }

  const removeOption = (fieldIndex: number, optionIndex: number) => {
    const field = fields[fieldIndex]
    if (!field) return
    updateProp(fieldIndex, 'options', options(field).filter((_, index) => index !== optionIndex))
  }

  /** 换控件类型 = 重建 props(旧类型的属性对新类型是未知属性,会直接判 propUnknown),只留通用字段。 */
  const changeType = (index: number, type: WfFormFieldType) => {
    updateField(index, (field) => ({
      ...createWfFormField(type, field.key),
      label: field.label,
      required: field.required,
      placeholder: field.placeholder,
    }))
  }

  /**
   * 人员关闭多选时必须同时清掉 `maxSelected`:校验对 `multiple !== true` 且带 maxSelected 的 user 字段
   * 直接判 propInvalid,只隐藏输入框会留下一条发布不掉的脏属性。
   */
  const setUserMultiple = (index: number, multiple: boolean) => {
    updateField(index, (field) => {
      const next = updateWfFormFieldProp(field, 'multiple', multiple)
      return multiple ? next : updateWfFormFieldProp(next, 'maxSelected', undefined)
    })
  }

  return (
    <div className="wf-form-designer">
      <div className="wf-form-toolbar">
        <Select
          className="wf-form-type-select"
          options={typeOptions}
          value={null}
          disabled={fields.length >= MAX_FIELDS}
          placeholder={t('workflow.form.addField')}
          onChange={(type: WfFormFieldType) => onChange(addWfFormField(value, type))}
        />
        <span className="wf-form-count">{fields.length}/{MAX_FIELDS}</span>
      </div>
      {schemaErrors.length ? <div className="wf-form-errors">{schemaErrors.join('；')}</div> : null}

      {fields.map((field, index) => (
        <Card
          // 索引键:字段 key 可空/可重复(编辑中),索引是这里唯一稳定的标识。
          key={index}
          size="small"
          className="wf-form-field"
          title={field.label || field.key || t('workflow.form.unnamedField')}
          extra={
            <Space size={4}>
              <Button
                type="text" shape="circle" size="small" disabled={index === 0}
                aria-label={t('workflow.form.moveUp')} icon={<AppIcon icon="ph:arrow-up" />}
                onClick={() => value && onChange(moveWfFormField(value, index, -1))}
              />
              <Button
                type="text" shape="circle" size="small" disabled={index === fields.length - 1}
                aria-label={t('workflow.form.moveDown')} icon={<AppIcon icon="ph:arrow-down" />}
                onClick={() => value && onChange(moveWfFormField(value, index, 1))}
              />
              <Button
                type="text" shape="circle" size="small" danger
                aria-label={t('workflow.form.removeField')} icon={<AppIcon icon="ph:trash" />}
                onClick={() => value && onChange(removeWfFormField(value, index))}
              />
            </Space>
          }
        >
          <div className="wf-form-grid">
            <Form.Item label={t('workflow.form.fieldType')}>
              <Select value={field.type} options={typeOptions} onChange={(type: WfFormFieldType) => changeType(index, type)} />
            </Form.Item>
            <Form.Item label={t('workflow.form.key')}>
              <Input
                value={field.key} maxLength={64}
                onChange={(e) => updateField(index, (current) => ({ ...current, key: e.target.value }))}
              />
            </Form.Item>
            <Form.Item label={t('workflow.form.label')}>
              <Input
                value={field.label} maxLength={128}
                onChange={(e) => updateField(index, (current) => ({ ...current, label: e.target.value }))}
              />
            </Form.Item>
            <Form.Item label={t('workflow.form.placeholder')}>
              <Input
                value={field.placeholder ?? ''} maxLength={256}
                onChange={(e) => updateField(index, (current) => ({ ...current, placeholder: e.target.value }))}
              />
            </Form.Item>
            <Form.Item label={t('workflow.form.required')}>
              <Switch
                checked={field.required}
                onChange={(checked) => updateField(index, (current) => ({ ...current, required: checked }))}
              />
            </Form.Item>
          </div>

          {field.type === 'text' || field.type === 'textarea' ? (
            <div className="wf-form-grid">
              <Form.Item label={t('workflow.form.maxLength')}>
                <InputNumber
                  className="w-full" value={numberProp(field, 'maxLength')}
                  min={1} max={field.type === 'text' ? 256 : 4000}
                  onChange={(v) => updateProp(index, 'maxLength', v)}
                />
              </Form.Item>
              {field.type === 'textarea' ? (
                <Form.Item label={t('workflow.form.rows')}>
                  <InputNumber
                    className="w-full" value={numberProp(field, 'rows')} min={2} max={8}
                    onChange={(v) => updateProp(index, 'rows', v)}
                  />
                </Form.Item>
              ) : null}
            </div>
          ) : field.type === 'number' || field.type === 'money' ? (
            <div className="wf-form-grid">
              <Form.Item label={t('workflow.form.min')}>
                <InputNumber className="w-full" value={numberProp(field, 'min')} onChange={(v) => updateProp(index, 'min', v)} />
              </Form.Item>
              <Form.Item label={t('workflow.form.max')}>
                <InputNumber className="w-full" value={numberProp(field, 'max')} onChange={(v) => updateProp(index, 'max', v)} />
              </Form.Item>
              {field.type === 'number' ? (
                <Form.Item label={t('workflow.form.precision')}>
                  <InputNumber
                    className="w-full" value={numberProp(field, 'precision')} min={0} max={6}
                    onChange={(v) => updateProp(index, 'precision', v)}
                  />
                </Form.Item>
              ) : null}
            </div>
          ) : field.type === 'date' || field.type === 'datetime' ? (
            <div className="wf-form-grid">
              <Form.Item label={t('workflow.form.min')}>
                <Input
                  value={stringProp(field, 'min')}
                  placeholder={field.type === 'date' ? 'YYYY-MM-DD' : '2026-01-01T00:00:00+08:00'}
                  onChange={(e) => updateProp(index, 'min', e.target.value)}
                />
              </Form.Item>
              <Form.Item label={t('workflow.form.max')}>
                <Input
                  value={stringProp(field, 'max')}
                  placeholder={field.type === 'date' ? 'YYYY-MM-DD' : '2026-12-31T23:59:59+08:00'}
                  onChange={(e) => updateProp(index, 'max', e.target.value)}
                />
              </Form.Item>
            </div>
          ) : field.type === 'select' || field.type === 'multiSelect' ? (
            <>
              {options(field).map((option, optionIndex) => (
                <div key={optionIndex} className="wf-form-option">
                  <Input
                    value={option.label} placeholder={t('workflow.form.optionLabel')}
                    onChange={(e) => updateOption(index, optionIndex, { label: e.target.value })}
                  />
                  <Input
                    value={option.value} placeholder={t('workflow.form.optionValue')}
                    onChange={(e) => updateOption(index, optionIndex, { value: e.target.value })}
                  />
                  <Button
                    type="text" shape="circle" aria-label={t('workflow.form.removeOption')}
                    icon={<AppIcon icon="ph:x" />} onClick={() => removeOption(index, optionIndex)}
                  />
                </div>
              ))}
              <Button type="dashed" block onClick={() => addOption(index)}>
                {t('workflow.form.addOption')}
              </Button>
              {field.type === 'multiSelect' ? (
                <Form.Item label={t('workflow.form.maxSelected')}>
                  <InputNumber
                    className="w-full" value={numberProp(field, 'maxSelected')} min={1} max={100}
                    onChange={(v) => updateProp(index, 'maxSelected', v)}
                  />
                </Form.Item>
              ) : null}
            </>
          ) : field.type === 'user' ? (
            <div className="wf-form-grid">
              <Form.Item label={t('workflow.form.multiple')}>
                <Switch checked={booleanProp(field, 'multiple')} onChange={(checked) => setUserMultiple(index, checked)} />
              </Form.Item>
              {booleanProp(field, 'multiple') ? (
                <Form.Item label={t('workflow.form.maxSelected')}>
                  <InputNumber
                    className="w-full" value={numberProp(field, 'maxSelected')} min={1} max={100}
                    onChange={(v) => updateProp(index, 'maxSelected', v)}
                  />
                </Form.Item>
              ) : null}
            </div>
          ) : field.type === 'attachment' ? (
            <div className="wf-form-grid">
              <Form.Item label={t('workflow.form.multiple')}>
                {/* 关闭多选时 updateWfFormFieldProp 会把 maxCount 一并压回 1(校验只允许 1)。 */}
                <Switch checked={booleanProp(field, 'multiple')} onChange={(checked) => updateProp(index, 'multiple', checked)} />
              </Form.Item>
              <Form.Item label={t('workflow.form.maxCount')}>
                <InputNumber
                  className="w-full" value={numberProp(field, 'maxCount')} min={1} max={20}
                  disabled={!booleanProp(field, 'multiple')}
                  onChange={(v) => updateProp(index, 'maxCount', v)}
                />
              </Form.Item>
              <Form.Item label={t('workflow.form.accept')}>
                <Input
                  value={stringProp(field, 'accept')} placeholder=".pdf,.png,.jpg"
                  onChange={(e) => updateProp(index, 'accept', e.target.value)}
                />
              </Form.Item>
              <Form.Item label={t('workflow.form.maxSizeMb')}>
                <InputNumber
                  className="w-full" value={numberProp(field, 'maxSizeMb')} min={1} max={100}
                  onChange={(v) => updateProp(index, 'maxSizeMb', v)}
                />
              </Form.Item>
            </div>
          ) : null}

          {fieldErrors(index).length ? <div className="wf-form-errors">{fieldErrors(index).join('；')}</div> : null}
        </Card>
      ))}
    </div>
  )
}
