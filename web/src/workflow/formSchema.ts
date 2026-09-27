import {
  WF_FORM_FIELD_TYPES,
  type WfFormField,
  type WfFormFieldOfType,
  type WfFormFieldPropsByType,
  type WfFormFieldType,
  type WfFormOption,
  type WfFormSchema,
  type WfButtonLabels,
  type WfModel,
  type WfNode,
  type WfNodeProps,
  type WfNodeType,
} from './schema'

export interface WfFormSchemaIssue {
  code: string
  fieldIndex?: number
  path: string
}

export function isWfFormSchema(value: unknown): value is WfFormSchema {
  return validateWfFormSchema(value).length === 0
}

/** 把后端 runtime projection 收窄为前端可消费的最小模型。 */
export function projectWfRuntimeModel(value: unknown): WfModel | null {
  if (!isRecord(value)) return null
  const root = projectRuntimeNode(value.root)
  if (!root) return null
  return {
    version: typeof value.version === 'number' ? value.version : 1,
    root,
    formSchema: projectRuntimeFormSchema(value.formSchema),
    formComponent: typeof value.formComponent === 'string' ? value.formComponent : null,
  }
}

function projectRuntimeFormSchema(value: unknown): WfFormSchema | null {
  if (!isRecord(value) || value.version !== 1 || !Array.isArray(value.fields)) return null
  const normalized = {
    version: 1 as const,
    fields: value.fields.map((field) => isRecord(field)
      ? { ...field, label: typeof field.label === 'string' ? field.label : '', required: field.required === true }
      : field),
  }
  return isWfFormSchema(normalized) ? normalized : null
}

function projectRuntimeNode(value: unknown): WfNode | null {
  if (!isRecord(value) || typeof value.id !== 'string' || typeof value.name !== 'string' || !isWfNodeType(value.type)) return null
  const next = value.next == null ? null : projectRuntimeNode(value.next)
  if (value.next != null && !next) return null
  const node: WfNode = { id: value.id, type: value.type, name: value.name, next, props: projectRuntimeProps(value.props) }
  if (value.type === 'branch' && Array.isArray(value.conditions)) {
    const arms = value.conditions.map(projectRuntimeBranchArm)
    if (arms.some((arm) => !arm)) return null
    node.conditions = arms.filter((arm): arm is NonNullable<typeof arm> => arm !== null)
  }
  if (value.type === 'parallel' && Array.isArray(value.parallelArms)) {
    const arms = value.parallelArms.map(projectRuntimeParallelArm)
    if (arms.some((arm) => !arm)) return null
    node.parallelArms = arms.filter((arm): arm is NonNullable<typeof arm> => arm !== null)
  }
  return node
}

function projectRuntimeBranchArm(value: unknown): NonNullable<WfNode['conditions']>[number] | null {
  if (!isRecord(value) || typeof value.id !== 'string' || typeof value.name !== 'string' || typeof value.isDefault !== 'boolean') return null
  const next = value.next == null ? null : projectRuntimeNode(value.next)
  return value.next != null && !next ? null : { id: value.id, name: value.name, isDefault: value.isDefault, next }
}

function projectRuntimeParallelArm(value: unknown): NonNullable<WfNode['parallelArms']>[number] | null {
  if (!isRecord(value) || typeof value.id !== 'string' || typeof value.name !== 'string') return null
  const next = value.next == null ? null : projectRuntimeNode(value.next)
  return value.next != null && !next ? null : { id: value.id, name: value.name, next }
}

function projectRuntimeProps(value: unknown): WfNodeProps | undefined {
  if (!isRecord(value)) return undefined
  const props: WfNodeProps = {}
  if (isRecord(value.assignee) && typeof value.assignee.provider === 'string') props.assignee = { provider: value.assignee.provider }
  if (value.returnPolicy === 'prev' || value.returnPolicy === 'any' || value.returnPolicy === 'node') props.returnPolicy = value.returnPolicy
  if (isRecord(value.buttonLabels)) {
    const labels: WfButtonLabels = {}
    for (const key of ['approve', 'reject', 'return', 'transfer', 'delegate', 'urge'] as const) {
      if (typeof value.buttonLabels[key] === 'string') labels[key] = value.buttonLabels[key]
    }
    props.buttonLabels = labels
  }
  if (Array.isArray(value.formPerms)) {
    props.formPerms = value.formPerms.filter((permission): permission is { field: string; access?: 'hidden' | 'readonly' | 'editable' } =>
      isRecord(permission) && typeof permission.field === 'string'
        && (permission.access == null || permission.access === 'hidden' || permission.access === 'readonly' || permission.access === 'editable'))
  }
  return props
}

function isWfNodeType(value: unknown): value is WfNodeType {
  return value === 'start' || value === 'approval' || value === 'cc' || value === 'branch' || value === 'parallel' || value === 'webhook' || value === 'aiDecision'
}

const defaults: { [T in WfFormFieldType]: WfFormFieldPropsByType[T] } = {
  text: { maxLength: 256 },
  textarea: { maxLength: 4000, rows: 4 },
  number: { precision: 0 },
  money: {},
  date: {},
  datetime: {},
  select: { options: [{ label: '选项 1', value: 'option1' }] },
  multiSelect: { options: [{ label: '选项 1', value: 'option1' }], maxSelected: 1 },
  user: { multiple: false },
  attachment: { multiple: false, maxCount: 1, maxSizeMb: 10 },
}

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
}

export function createWfFormField(type: 'text', key?: string): WfFormFieldOfType<'text'>
export function createWfFormField(type: 'textarea', key?: string): WfFormFieldOfType<'textarea'>
export function createWfFormField(type: 'number', key?: string): WfFormFieldOfType<'number'>
export function createWfFormField(type: 'money', key?: string): WfFormFieldOfType<'money'>
export function createWfFormField(type: 'date', key?: string): WfFormFieldOfType<'date'>
export function createWfFormField(type: 'datetime', key?: string): WfFormFieldOfType<'datetime'>
export function createWfFormField(type: 'select', key?: string): WfFormFieldOfType<'select'>
export function createWfFormField(type: 'multiSelect', key?: string): WfFormFieldOfType<'multiSelect'>
export function createWfFormField(type: 'user', key?: string): WfFormFieldOfType<'user'>
export function createWfFormField(type: 'attachment', key?: string): WfFormFieldOfType<'attachment'>
export function createWfFormField(type: WfFormFieldType, key?: string): WfFormField
export function createWfFormField(type: WfFormFieldType, key: string = type): WfFormField {
  const base = { key, label: '', required: false as const }
  switch (type) {
    case 'text': return { ...base, type, props: clone(defaults.text) }
    case 'textarea': return { ...base, type, props: clone(defaults.textarea) }
    case 'number': return { ...base, type, props: clone(defaults.number) }
    case 'money': return { ...base, type, props: clone(defaults.money) }
    case 'date': return { ...base, type, props: clone(defaults.date) }
    case 'datetime': return { ...base, type, props: clone(defaults.datetime) }
    case 'select': return { ...base, type, props: clone(defaults.select) }
    case 'multiSelect': return { ...base, type, props: clone(defaults.multiSelect) }
    case 'user': return { ...base, type, props: clone(defaults.user) }
    case 'attachment': return { ...base, type, props: clone(defaults.attachment) }
  }
}

export function addWfFormField(schema: WfFormSchema | null, type: WfFormFieldType): WfFormSchema {
  const fields = schema?.fields ?? []
  let suffix = 1
  while (fields.some((field) => field.key === `${type}${suffix}`)) suffix += 1
  return { version: 1, fields: [...fields, createWfFormField(type, `${type}${suffix}`)] }
}

export function removeWfFormField(schema: WfFormSchema, index: number): WfFormSchema | null {
  if (index < 0 || index >= schema.fields.length) return schema
  const fields = schema.fields.filter((_, fieldIndex) => fieldIndex !== index)
  return fields.length ? { version: 1, fields } : null
}

export function moveWfFormField(schema: WfFormSchema, index: number, offset: -1 | 1): WfFormSchema {
  const target = index + offset
  if (index < 0 || index >= schema.fields.length || target < 0 || target >= schema.fields.length) return schema
  const fields = [...schema.fields]
  ;[fields[index], fields[target]] = [fields[target]!, fields[index]!]
  return { version: 1, fields }
}

export function serializeWfFormSchema(schema: WfFormSchema | null | undefined): WfFormSchema | null {
  if (!schema?.fields.length) return null
  return {
    version: 1,
    fields: schema.fields.map((field) => {
      const cloned = clone(field)
      return { ...cloned, label: field.label.trim(), placeholder: field.placeholder?.trim() || undefined }
    }),
  }
}

export function validateWfFormSchema(schema: unknown): WfFormSchemaIssue[] {
  const issues: WfFormSchemaIssue[] = []
  const add = (code: string, path: string, fieldIndex?: number) => issues.push({ code, path, fieldIndex })
  if (!isRecord(schema) || schema.version !== 1 || !Array.isArray(schema.fields)) {
    add('schemaInvalid', 'formSchema')
    return issues
  }
  if (schema.fields.length < 1 || schema.fields.length > 50) add('fieldCountInvalid', 'fields')
  if (new TextEncoder().encode(JSON.stringify(schema)).length > 64 * 1024) add('schemaTooLarge', 'formSchema')

  const keys = new Set<string>()
  schema.fields.forEach((raw, index) => {
    const base = `fields.${index}`
    if (!isRecord(raw)) {
      add('fieldInvalid', base, index)
      return
    }
    const key = raw.key
    if (typeof key !== 'string' || !/^[A-Za-z][A-Za-z0-9_]{0,63}$/.test(key)) add('keyInvalid', `${base}.key`, index)
    else if (keys.has(key)) add('keyDuplicate', `${base}.key`, index)
    else keys.add(key)

    if (typeof raw.label !== 'string' || !validTrimmedText(raw.label, 128, false)) add('labelInvalid', `${base}.label`, index)
    if (typeof raw.required !== 'boolean') add('requiredInvalid', `${base}.required`, index)
    if (raw.placeholder != null && (typeof raw.placeholder !== 'string' || !validTrimmedText(raw.placeholder, 256, true))) {
      add('placeholderInvalid', `${base}.placeholder`, index)
    }
    if (!WF_FORM_FIELD_TYPES.includes(raw.type as WfFormFieldType)) {
      add('typeInvalid', `${base}.type`, index)
      return
    }
    validateProps(raw.type as WfFormFieldType, raw.props, base, index, add)
  })
  return issues
}

function validateProps(
  type: WfFormFieldType,
  raw: unknown,
  base: string,
  index: number,
  add: (code: string, path: string, fieldIndex?: number) => void,
) {
  if (raw == null) {
    if (type === 'select' || type === 'multiSelect') add('optionsRequired', `${base}.props.options`, index)
    return
  }
  if (!isRecord(raw)) {
    add('propsInvalid', `${base}.props`, index)
    return
  }
  const allowed: Record<WfFormFieldType, string[]> = {
    text: ['maxLength'], textarea: ['maxLength', 'rows'], number: ['min', 'max', 'precision'], money: ['min', 'max'],
    date: ['min', 'max'], datetime: ['min', 'max'], select: ['options'], multiSelect: ['options', 'maxSelected'],
    user: ['multiple', 'maxSelected'], attachment: ['multiple', 'maxCount', 'accept', 'maxSizeMb'],
  }
  if (Object.keys(raw).some((key) => !allowed[type].includes(key))) add('propUnknown', `${base}.props`, index)
  const integer = (key: string, min: number, max: number) => {
    const value = raw[key]
    if (value !== undefined && (!Number.isInteger(value) || (value as number) < min || (value as number) > max)) {
      add('propInvalid', `${base}.props.${key}`, index)
    }
  }
  const number = (key: string) => {
    const value = raw[key]
    if (value !== undefined && (typeof value !== 'number' || !Number.isFinite(value))) add('propInvalid', `${base}.props.${key}`, index)
  }
  if (type === 'text') integer('maxLength', 1, 256)
  if (type === 'textarea') { integer('maxLength', 1, 4000); integer('rows', 2, 8) }
  if (type === 'number') { number('min'); number('max'); integer('precision', 0, 6); validateRange(raw, base, index, add) }
  if (type === 'money') {
    number('min'); number('max'); validateRange(raw, base, index, add)
    for (const key of ['min', 'max']) if (typeof raw[key] === 'number' && Math.round((raw[key] as number) * 100) !== (raw[key] as number) * 100) add('propInvalid', `${base}.props.${key}`, index)
  }
  if (type === 'date') validateStringRange(raw, /^\d{4}-\d{2}-\d{2}$/, true, base, index, add, validDate)
  if (type === 'datetime') validateStringRange(raw, /^\d{4}-\d{2}-\d{2}T.+(?:Z|[+-]\d{2}:\d{2})$/, true, base, index, add, validDateTime)
  if (type === 'select' || type === 'multiSelect') {
    const count = validateOptions(raw.options, base, index, add)
    if (type === 'multiSelect') integer('maxSelected', 1, Math.min(100, count || 100))
  }
  if (type === 'user') {
    if (raw.multiple !== undefined && typeof raw.multiple !== 'boolean') add('propInvalid', `${base}.props.multiple`, index)
    if (raw.multiple === true) integer('maxSelected', 1, 100)
    else if (raw.maxSelected !== undefined) add('propInvalid', `${base}.props.maxSelected`, index)
  }
  if (type === 'attachment') {
    if (raw.multiple !== undefined && typeof raw.multiple !== 'boolean') add('propInvalid', `${base}.props.multiple`, index)
    integer('maxCount', 1, 20); integer('maxSizeMb', 1, 100)
    if (raw.multiple !== true && raw.maxCount !== undefined && raw.maxCount !== 1) add('propInvalid', `${base}.props.maxCount`, index)
    if (raw.accept !== undefined && (typeof raw.accept !== 'string' || raw.accept.length > 256 || hasControl(raw.accept) || !validAttachmentAccept(raw.accept))) add('propInvalid', `${base}.props.accept`, index)
  }
}

function validAttachmentAccept(value: string): boolean {
  if (!value.trim()) return true
  const tokens = value.split(',').map((token) => token.trim()).filter(Boolean)
  return tokens.length > 0 && tokens.every((token) => /^\.[A-Za-z0-9]+$/.test(token))
}

function validateRange(raw: Record<string, unknown>, base: string, index: number, add: (code: string, path: string, fieldIndex?: number) => void) {
  if (typeof raw.min === 'number' && typeof raw.max === 'number' && raw.min > raw.max) add('rangeInvalid', `${base}.props`, index)
}

function validateStringRange(raw: Record<string, unknown>, format: RegExp, parse: boolean, base: string, index: number, add: (code: string, path: string, fieldIndex?: number) => void, valid = (value: string) => Number.isFinite(Date.parse(value))) {
  for (const key of ['min', 'max']) {
    const value = raw[key]
    if (value !== undefined && (typeof value !== 'string' || !format.test(value) || (parse && !valid(value)))) add('propInvalid', `${base}.props.${key}`, index)
  }
  if (typeof raw.min === 'string' && typeof raw.max === 'string'
    && (parse ? Date.parse(raw.min) > Date.parse(raw.max) : raw.min > raw.max)) add('rangeInvalid', `${base}.props`, index)
}

function validDate(value: string): boolean {
  const [year, month, day] = value.split('-').map(Number)
  if (year! < 1 || year! > 9999 || month! < 1 || month! > 12 || day! < 1 || day! > 31) return false
  const date = new Date(0)
  date.setUTCFullYear(year!, month! - 1, day)
  date.setUTCHours(0, 0, 0, 0)
  return date.getUTCFullYear() === year && date.getUTCMonth() === month! - 1 && date.getUTCDate() === day
}

function validDateTime(value: string): boolean {
  const datePart = /^(\d{4}-\d{2}-\d{2})T/.exec(value)?.[1]
  if (!datePart || !validDate(datePart)) return false
  const offset = /[+-](\d{2}):(\d{2})$/.exec(value)
  if (offset) {
    const hours = Number(offset[1])
    const minutes = Number(offset[2])
    if (hours > 14 || minutes > 59 || (hours === 14 && minutes !== 0)) return false
  }
  return Number.isFinite(Date.parse(value))
}

function validateOptions(raw: unknown, base: string, index: number, add: (code: string, path: string, fieldIndex?: number) => void): number {
  if (!Array.isArray(raw) || raw.length < 1 || raw.length > 100) {
    add('optionsInvalid', `${base}.props.options`, index)
    return 0
  }
  const values = new Set<string>()
  raw.forEach((option: unknown, optionIndex: number) => {
    const path = `${base}.props.options.${optionIndex}`
    if (!isRecord(option) || Object.keys(option).length !== 2 || !validTrimmedText(option.label, 128, false)
      || !validTrimmedText(option.value, 64, false)) add('optionInvalid', path, index)
    else if (values.has(option.value as string)) add('optionDuplicate', `${path}.value`, index)
    else values.add(option.value as string)
  })
  return raw.length
}

function validTrimmedText(value: unknown, max: number, emptyAllowed: boolean): boolean {
  if (typeof value !== 'string' || hasControl(value)) return false
  const length = value.trim().length
  return (emptyAllowed || length > 0) && length <= max
}

function hasControl(value: string): boolean {
  return [...value].some((character) => {
    const code = character.charCodeAt(0)
    return code <= 0x1f || code === 0x7f
  })
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

export function getWfFormFieldProp(field: WfFormField, key: string): unknown {
  if (!field.props) return undefined
  switch (field.type) {
    case 'text': case 'textarea': return key === 'maxLength' ? field.props.maxLength : field.type === 'textarea' && key === 'rows' ? field.props.rows : undefined
    case 'number': return key === 'min' ? field.props.min : key === 'max' ? field.props.max : key === 'precision' ? field.props.precision : undefined
    case 'money': return key === 'min' ? field.props.min : key === 'max' ? field.props.max : undefined
    case 'date': case 'datetime': return key === 'min' ? field.props.min : key === 'max' ? field.props.max : undefined
    case 'select': case 'multiSelect': return key === 'options' ? field.props.options : field.type === 'multiSelect' && key === 'maxSelected' ? field.props.maxSelected : undefined
    case 'user': return key === 'multiple' ? field.props.multiple : key === 'maxSelected' ? field.props.maxSelected : undefined
    case 'attachment': return key === 'multiple' ? field.props.multiple : key === 'maxCount' ? field.props.maxCount : key === 'accept' ? field.props.accept : key === 'maxSizeMb' ? field.props.maxSizeMb : undefined
  }
}

export function updateWfFormFieldProp(field: WfFormField, key: string, value: unknown): WfFormField {
  const empty = value === null || value === undefined || value === ''
  const numberValue = empty ? undefined : typeof value === 'number' ? value : undefined
  const stringValue = empty ? undefined : typeof value === 'string' ? value : undefined
  const booleanValue = empty ? undefined : typeof value === 'boolean' ? value : undefined
  switch (field.type) {
    case 'text': return key === 'maxLength' ? { ...field, props: { ...field.props, maxLength: numberValue } } : field
    case 'textarea': return key === 'maxLength' ? { ...field, props: { ...field.props, maxLength: numberValue } } : key === 'rows' ? { ...field, props: { ...field.props, rows: numberValue } } : field
    case 'number': return key === 'min' ? { ...field, props: { ...field.props, min: numberValue } } : key === 'max' ? { ...field, props: { ...field.props, max: numberValue } } : key === 'precision' ? { ...field, props: { ...field.props, precision: numberValue } } : field
    case 'money': return key === 'min' ? { ...field, props: { ...field.props, min: numberValue } } : key === 'max' ? { ...field, props: { ...field.props, max: numberValue } } : field
    case 'date': case 'datetime': return key === 'min' ? { ...field, props: { ...field.props, min: stringValue } } : key === 'max' ? { ...field, props: { ...field.props, max: stringValue } } : field
    case 'select': return key === 'options' && Array.isArray(value) ? { ...field, props: { ...field.props, options: value } } : field
    case 'multiSelect': return key === 'options' && Array.isArray(value) ? { ...field, props: { ...field.props, options: value } } : key === 'maxSelected' ? { ...field, props: { ...field.props, options: field.props?.options ?? [], maxSelected: numberValue } } : field
    case 'user': {
      if (key === 'maxSelected') return { ...field, props: { ...field.props, maxSelected: numberValue } }
      if (key !== 'multiple') return field
      // 关掉多选后 maxSelected 不再是合法属性(validateProps 只要见到这个键就判 propInvalid),
      // 必须把键摘掉——与附件关多选时把 maxCount 收回 1 同一个道理。
      const props = { ...field.props, multiple: booleanValue }
      if (booleanValue !== true) delete props.maxSelected
      return { ...field, props }
    }
    case 'attachment': return key === 'multiple' ? { ...field, props: { ...field.props, multiple: booleanValue, ...(booleanValue !== true ? { maxCount: 1 } : {}) } } : key === 'maxCount' ? { ...field, props: { ...field.props, maxCount: numberValue } } : key === 'accept' ? { ...field, props: { ...field.props, accept: stringValue } } : key === 'maxSizeMb' ? { ...field, props: { ...field.props, maxSizeMb: numberValue } } : field
  }
}

export function setWfFormOptions(field: WfFormField, options: WfFormOption[]): WfFormField {
  if (field.type !== 'select' && field.type !== 'multiSelect') return field
  return { ...field, props: { ...field.props, options } }
}
