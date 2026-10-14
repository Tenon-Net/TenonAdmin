import type { WfFormField, WfFormFieldPerm, WfFormPermAccess, WfFormSchema } from './schema'

export type WfFormValues = Record<string, unknown>

export interface WfFormValueIssue {
  key: string
  code: 'required' | 'valueInvalid' | 'rangeInvalid' | 'optionInvalid' | 'userInvalid' | 'attachmentInvalid'
}

export interface WfFormParseResult {
  values: WfFormValues
  error: 'invalidJson' | 'objectRequired' | null
}

export function parseWfFormValues(json: string | null | undefined): WfFormValues {
  return parseWfFormValuesResult(json).values
}

export function parseWfFormValuesResult(json: string | null | undefined): WfFormParseResult {
  if (!json?.trim()) return { values: {}, error: null }
  try {
    const parsed: unknown = JSON.parse(json)
    if (!isRecord(parsed) || Array.isArray(parsed)) return { values: {}, error: 'objectRequired' }
    const values: WfFormValues = {}
    for (const [key, value] of Object.entries(parsed)) {
      if (key !== '__proto__') values[key] = value
    }
    return { values, error: null }
  } catch {
    return { values: {}, error: 'invalidJson' }
  }
}

export function serializeWfFormValues(values: WfFormValues | null | undefined): string | null {
  if (!values) return null
  const entries = Object.entries(values).filter(([, value]) => value !== undefined)
  return entries.length ? JSON.stringify(Object.fromEntries(entries)) : null
}

export function validateWfFormValues(
  schema: WfFormSchema,
  values: WfFormValues,
  permissions?: WfFormFieldPerm[] | null,
): WfFormValueIssue[] {
  const issues: WfFormValueIssue[] = []
  for (const field of schema.fields) {
    if (formFieldAccess(field.key, permissions) !== 'editable') continue
    const value = values[field.key]
    if (field.required && isEmpty(value)) {
      issues.push({ key: field.key, code: 'required' })
      continue
    }
    if (isEmpty(value)) continue
    if (field.type === 'text' || field.type === 'textarea') {
      const maxLength = field.props?.maxLength ?? (field.type === 'text' ? 256 : 4000)
      if (typeof value !== 'string' || value.length > maxLength) issues.push({ key: field.key, code: 'valueInvalid' })
    } else if (field.type === 'number' || field.type === 'money') {
      if (!isFiniteNumber(value)) {
        issues.push({ key: field.key, code: 'valueInvalid' })
      } else {
        const props = (field.props as { min?: number; max?: number; precision?: number } | null | undefined) ?? {}
        const precision = field.type === 'money' ? 2 : props.precision
        const min = props.min ?? (field.type === 'money' ? 0 : undefined)
        if (precision !== undefined && !hasAtMostDecimals(value, precision)) issues.push({ key: field.key, code: 'valueInvalid' })
        else if ((min !== undefined && value < min) || (props.max !== undefined && value > props.max)) {
          issues.push({ key: field.key, code: 'rangeInvalid' })
        }
      }
    } else if (field.type === 'date') {
      if (typeof value !== 'string' || !isDate(value)) issues.push({ key: field.key, code: 'valueInvalid' })
      else if ((field.props?.min && value < field.props.min) || (field.props?.max && value > field.props.max)) {
        issues.push({ key: field.key, code: 'rangeInvalid' })
      }
    } else if (field.type === 'datetime') {
      if (typeof value !== 'string' || !isDateTime(value)) issues.push({ key: field.key, code: 'valueInvalid' })
      else {
        const timestamp = Date.parse(value)
        const min = field.props?.min ? Date.parse(field.props.min) : undefined
        const max = field.props?.max ? Date.parse(field.props.max) : undefined
        if ((min !== undefined && timestamp < min) || (max !== undefined && timestamp > max)) {
          issues.push({ key: field.key, code: 'rangeInvalid' })
        }
      }
    } else if (field.type === 'select') {
      if (typeof value !== 'string' || !field.props?.options.some((option) => option.value === value)) {
        issues.push({ key: field.key, code: 'optionInvalid' })
      }
    } else if (field.type === 'multiSelect') {
      const valuesList = value as unknown
      const options = field.props?.options ?? []
      const maxSelected = field.props?.maxSelected ?? options.length
      if (!Array.isArray(valuesList)
        || valuesList.some((item) => typeof item !== 'string' || !options.some((option) => option.value === item))
        || new Set(valuesList).size !== valuesList.length
        || valuesList.length > maxSelected) {
        issues.push({ key: field.key, code: 'optionInvalid' })
      }
    } else if (field.type === 'user') {
      const multiple = field.props?.multiple === true
      const ids = multiple ? value : [value]
      const maxSelected = field.props?.maxSelected ?? 20
      if (!Array.isArray(ids)
        || ids.length === 0
        || ids.some((id) => !isPositiveId(id))
        || new Set(ids.map(String)).size !== ids.length
        || (multiple && ids.length > maxSelected)) {
        issues.push({ key: field.key, code: 'userInvalid' })
      }
    } else if (field.type === 'attachment') {
      const multiple = field.props?.multiple === true
      const ids = multiple ? value : [value]
      const maxCount = multiple ? (field.props?.maxCount ?? 20) : 1
      if (!Array.isArray(ids)
        || ids.length === 0
        || ids.some((id) => !isPositiveId(id))
        || new Set(ids.map(String)).size !== ids.length
        || ids.length > maxCount) {
        issues.push({ key: field.key, code: 'attachmentInvalid' })
      }
    }
  }
  return issues
}

function isEmpty(value: unknown): boolean {
  return value == null || (typeof value === 'string' && value.trim() === '') || (Array.isArray(value) && value.length === 0)
}

function isRecord(value: unknown): value is WfFormValues {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

function hasAtMostDecimals(value: number, decimals: number): boolean {
  const scale = 10 ** decimals
  return Math.abs(value * scale - Math.round(value * scale)) < Number.EPSILON * Math.max(1, Math.abs(value * scale)) * 8
}

function isDate(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (!match) return false
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  const date = new Date(0)
  date.setUTCFullYear(year, month - 1, day)
  date.setUTCHours(0, 0, 0, 0)
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
}

function isDateTime(value: string): boolean {
  return /T/.test(value) && /(?:Z|[+-]\d{2}:\d{2})$/.test(value) && Number.isFinite(Date.parse(value))
}

function isPositiveId(value: unknown): boolean {
  if (typeof value === 'number') return Number.isSafeInteger(value) && value > 0
  return typeof value === 'string' && /^[1-9]\d*$/.test(value)
}

export function formFieldValue(field: WfFormField, values: WfFormValues): unknown {
  return values[field.key]
}

const formPermissionRank: Record<WfFormPermAccess, number> = { hidden: 0, readonly: 1, editable: 2 }

/**
 * 观察者(没有自己的待办或审批历史)的字段权限。
 * 只有每个适用审批节点都明确写成 hidden 时才隐藏;没写权限的节点视为可编辑。
 */
export function observerHiddenFields(
  nodes: ReadonlyArray<{ type?: string; props?: { formPerms?: WfFormFieldPerm[] | null } | null } | null | undefined>,
  fieldKeys: readonly string[],
): WfFormFieldPerm[] {
  const approval = nodes.filter((node) => node?.type === 'approval')
  if (approval.length === 0) return []
  return fieldKeys.flatMap((field) => {
    const hiddenEverywhere = approval.every((node) =>
      (node?.props?.formPerms ?? []).some((permission) =>
        permission.field === field && (permission.access ?? 'editable') === 'hidden'))
    return hiddenEverywhere ? [{ field, access: 'hidden' as const }] : []
  })
}

export function mergeWfFormPermissions(
  ...permissionSets: Array<readonly WfFormFieldPerm[] | null | undefined>
): WfFormFieldPerm[] {
  const merged = new Map<string, WfFormFieldPerm>()
  for (const permissions of permissionSets) {
    for (const permission of permissions ?? []) {
      const access = permission.access ?? 'editable'
      const current = merged.get(permission.field)
      if (!current || formPermissionRank[access] < formPermissionRank[current.access ?? 'editable']) {
        merged.set(permission.field, { field: permission.field, access })
      }
    }
  }
  return [...merged.values()]
}

export function formFieldAccess(
  field: string,
  permissions?: WfFormFieldPerm[] | null,
): WfFormPermAccess {
  // 未传权限(发起校验)仍按可编辑。节点给出了权限表时,没点名的字段保持只读。
  if (permissions == null) return 'editable'
  const matches = permissions.filter((permission) => permission.field === field)
  if (matches.length === 0) return 'readonly'
  return matches.reduce<WfFormPermAccess>(
    (best, permission) => formPermissionRank[permission.access ?? 'editable'] < formPermissionRank[best]
      ? permission.access ?? 'editable'
      : best,
    'editable',
  )
}
