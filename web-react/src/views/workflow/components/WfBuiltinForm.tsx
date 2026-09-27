// 内置表单运行时:10 种控件的可编辑渲染、提交前校验与只读回放。对应 Vue 侧 WfBuiltinForm.vue。
// 受控组件:值住在 WfFormMount,这里只负责「能不能改」「怎么渲染」「校验结果落到哪个字段」。
import { forwardRef, useImperativeHandle, useState } from 'react'
import { Button, DatePicker, Form, Input, InputNumber, Select, Space, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { useTranslation } from 'react-i18next'
import { AppIcon } from '@/components/AppIcon'
import { FileUpload } from '@/components/FileUpload'
import { UserSelect } from '@/components/UserSelect'
import type { FileUploadOutput } from '@/types/api'
import {
  formFieldAccess,
  formFieldValue,
  validateWfFormValues,
  type WfFormValueIssue,
  type WfFormValues,
} from '@/workflow/formRuntime'
import type { WfFormField, WfFormFieldPerm, WfFormOption, WfFormSchema } from '@/workflow/schema'

/** 发起 / 办理 / 查看:决定字段权限是否参与判定,以及控件是否可编辑。 */
export type WfFormMode = 'start' | 'approve' | 'view'

export interface WfBuiltinFormHandle {
  /** 提交前校验;返回 false 时错误已落到各字段下方。 */
  validate: () => boolean
}

export interface WfBuiltinFormProps {
  schema: WfFormSchema
  value: WfFormValues
  mode: WfFormMode
  permissions?: WfFormFieldPerm[] | null
  onChange: (values: WfFormValues) => void
}

const DATE_FORMAT = 'YYYY-MM-DD'
/** 与 formRuntime.isDateTime 的要求对齐:必须带 T 且以 Z 或 ±HH:mm 收尾。 */
const DATETIME_FORMAT = 'YYYY-MM-DD[T]HH:mm:ssZ'

/**
 * 用户 / 附件 Id 是后端 long 雪花:JSON 既可能给 number 也可能给十进制 string。
 * 判定与 formRuntime 内部的 isPositiveId 同规则(number 要求安全整数,string 要求十进制正整数),
 * 但**不做 Number() 强转** —— 19 位雪花过一遍 Number() 就掉精度,存回去是另一个文件。
 */
function normalizeId(raw: number | string | null | undefined): number | string | null {
  if (typeof raw === 'number') return Number.isSafeInteger(raw) && raw > 0 ? raw : null
  if (typeof raw === 'string' && /^[1-9]\d*$/.test(raw.trim())) return raw.trim()
  return null
}

function optionsOf(field: WfFormField): WfFormOption[] {
  return field.props && 'options' in field.props ? field.props.options : []
}

function numericPrecision(field: WfFormField): number | undefined {
  if (field.type === 'money') return 2
  return field.type === 'number' ? field.props?.precision : undefined
}

export const WfBuiltinForm = forwardRef<WfBuiltinFormHandle, WfBuiltinFormProps>(function WfBuiltinForm(
  { schema, value, mode, permissions, onChange },
  ref,
) {
  const { t } = useTranslation()
  const [issues, setIssues] = useState<WfFormValueIssue[]>([])

  const canEdit = (field: WfFormField) =>
    mode === 'start' || (mode === 'approve' && formFieldAccess(field.key, permissions) === 'editable')

  const isHidden = (field: WfFormField) =>
    mode !== 'start' && formFieldAccess(field.key, permissions) === 'hidden'

  function updateValue(field: WfFormField, next: unknown) {
    if (!canEdit(field)) return
    const values: WfFormValues = { ...value }
    if (next === undefined) delete values[field.key]
    else values[field.key] = next
    setIssues((prev) => prev.filter((issue) => issue.key !== field.key))
    onChange(values)
  }

  useImperativeHandle(ref, () => ({
    validate: () => {
      // 发起态没有节点字段权限可言(权限挂在审批节点上),按全字段校验。
      const found = validateWfFormValues(schema, value, mode === 'start' ? undefined : permissions)
      setIssues(found)
      return found.length === 0
    },
  }), [schema, value, mode, permissions])

  function valueOf(field: WfFormField): unknown {
    return formFieldValue(field, value)
  }

  function stringValue(field: WfFormField): string | undefined {
    const raw = valueOf(field)
    return typeof raw === 'string' ? raw : undefined
  }

  function numberValue(field: WfFormField): number | null {
    const raw = valueOf(field)
    return typeof raw === 'number' ? raw : null
  }

  function attachmentIds(field: WfFormField): Array<number | string> {
    const raw = valueOf(field)
    const list = field.type === 'attachment' && field.props?.multiple === true
      ? Array.isArray(raw) ? raw : []
      : [raw]
    return list
      .map((id) => normalizeId(id as number | string | null))
      .filter((id): id is number | string => id !== null)
  }

  function attachmentMaxCount(field: WfFormField): number {
    return field.type === 'attachment' && field.props?.multiple === true ? (field.props.maxCount ?? 20) : 1
  }

  function onUploaded(field: WfFormField, output: FileUploadOutput) {
    const id = normalizeId(output.id)
    if (id === null) return
    if (field.type !== 'attachment' || field.props?.multiple !== true) {
      updateValue(field, id)
      return
    }
    const maxCount = field.props?.maxCount ?? 20
    const ids = attachmentIds(field)
    if (!ids.some((current) => String(current) === String(id))) {
      updateValue(field, [...ids, id].slice(0, maxCount))
    }
  }

  function removeAttachment(field: WfFormField, id: number | string) {
    const ids = attachmentIds(field).filter((current) => String(current) !== String(id))
    updateValue(field, field.type === 'attachment' && field.props?.multiple === true ? ids : null)
  }

  function onUserChange(field: WfFormField, next: unknown) {
    if (field.type === 'user' && field.props?.multiple === true && Array.isArray(next)) {
      updateValue(field, next.slice(0, field.props.maxSelected ?? 20))
      return
    }
    updateValue(field, next)
  }

  function control(field: WfFormField, id: string) {
    const disabled = !canEdit(field)
    const placeholder = field.placeholder ?? undefined
    switch (field.type) {
      case 'text':
        return (
          <Input
            id={id} value={stringValue(field)} disabled={disabled} placeholder={placeholder}
            maxLength={field.props?.maxLength ?? 256}
            onChange={(e) => updateValue(field, e.target.value)}
          />
        )
      case 'textarea':
        return (
          <Input.TextArea
            id={id} value={stringValue(field)} disabled={disabled} placeholder={placeholder}
            rows={field.props?.rows ?? 4} maxLength={field.props?.maxLength ?? 4000}
            onChange={(e) => updateValue(field, e.target.value)}
          />
        )
      case 'number':
      case 'money':
        return (
          <InputNumber
            id={id} value={numberValue(field)} disabled={disabled} placeholder={placeholder}
            min={field.props?.min} max={field.props?.max} precision={numericPrecision(field)}
            style={{ width: '100%' }}
            onChange={(next) => updateValue(field, next ?? null)}
          />
        )
      case 'date':
        return (
          <DatePicker
            id={id} disabled={disabled} placeholder={placeholder} style={{ width: '100%' }}
            value={parseDate(stringValue(field))}
            onChange={(d) => updateValue(field, d ? d.format(DATE_FORMAT) : null)}
          />
        )
      case 'datetime':
        return (
          <DatePicker
            id={id} showTime disabled={disabled} placeholder={placeholder} style={{ width: '100%' }}
            value={parseDate(stringValue(field))}
            onChange={(d) => updateValue(field, d ? d.format(DATETIME_FORMAT) : null)}
          />
        )
      case 'select':
        return (
          <Select
            id={id} disabled={disabled} placeholder={placeholder} allowClear
            value={stringValue(field) ?? null}
            options={optionsOf(field)}
            onChange={(next: string | null) => updateValue(field, next ?? null)}
          />
        )
      case 'multiSelect':
        return (
          <Select
            id={id} mode="multiple" disabled={disabled} placeholder={placeholder}
            value={Array.isArray(valueOf(field)) ? (valueOf(field) as string[]) : []}
            options={optionsOf(field)}
            maxCount={field.props?.maxSelected}
            maxTagCount={field.props?.maxSelected}
            onChange={(next: string[]) => updateValue(field, next)}
          />
        )
      case 'user': {
        const multiple = field.props?.multiple === true
        const raw = valueOf(field)
        return (
          <UserSelect
            id={id} disabled={disabled} placeholder={placeholder}
            mode={multiple ? 'multiple' : undefined}
            value={multiple ? (Array.isArray(raw) ? (raw as Array<number | string>) : []) : ((raw as number | string | undefined) ?? undefined)}
            onChange={(next: unknown) => onUserChange(field, next)}
          />
        )
      }
      case 'attachment': {
        const ids = attachmentIds(field)
        const full = ids.length >= attachmentMaxCount(field)
        return (
          <Space orientation="vertical" size={8} style={{ width: '100%' }}>
            {!disabled && !full ? (
              <FileUpload
                accept={field.props?.accept}
                multiple={field.props?.multiple === true}
                maxCount={attachmentMaxCount(field) - ids.length}
                showUploadList={false}
                onUploaded={(output) => onUploaded(field, output)}
              >
                <Button size="small" icon={<AppIcon icon="ph:upload-simple" size={14} />}>{t('file.upload')}</Button>
              </FileUpload>
            ) : null}
            {ids.length > 0 ? (
              <Space size={8} wrap>
                {ids.map((fileId) => (
                  <Tag
                    key={String(fileId)}
                    closable={!disabled}
                    onClose={() => removeAttachment(field, fileId)}
                  >
                    {String(fileId)}
                  </Tag>
                ))}
              </Space>
            ) : disabled ? (
              <Typography.Text type="secondary">—</Typography.Text>
            ) : null}
          </Space>
        )
      }
    }
  }

  return (
    <Form layout="vertical" className="wf-builtin-form" component="div">
      {schema.fields.filter((field) => !isHidden(field)).map((field) => {
        const issue = issues.find((item) => item.key === field.key)
        const id = `wf-field-${field.key}`
        return (
          <Form.Item
            key={field.key}
            htmlFor={id}
            label={field.label || field.key}
            required={field.required && canEdit(field)}
            validateStatus={issue ? 'error' : undefined}
            help={issue ? t(`workflow.form.runtime.${issue.code}`) : undefined}
          >
            {control(field, id)}
          </Form.Item>
        )
      })}
    </Form>
  )
})

/** 只有能被 dayjs 解析的字符串才给 DatePicker;脏数据回显成空而不是 Invalid Date。 */
function parseDate(raw: string | undefined): dayjs.Dayjs | null {
  if (!raw) return null
  const parsed = dayjs(raw)
  return parsed.isValid() ? parsed : null
}
