// 发起页的纯逻辑(变异钉):摘要变量的键值行 → variablesJson。与 Vue 侧 start/index.vue 同规则。
export interface WfVarRow {
  key?: string
  value?: string
}

/** 'true'/'false' → 布尔,纯数字串 → 数字,其余原样(空串保留空串)。 */
export function coerceVarValue(raw: string | undefined): unknown {
  const s = (raw ?? '').trim()
  if (s === '') return ''
  if (s === 'true') return true
  if (s === 'false') return false
  if (/^-?\d+(\.\d+)?$/.test(s)) return Number(s)
  return raw
}

/** 无有效键则返回 null(后端把 null 当"没有摘要变量",空对象会被当成一份空变量集)。 */
export function serializeVars(rows: readonly WfVarRow[]): string | null {
  const obj: Record<string, unknown> = Object.create(null) as Record<string, unknown>
  for (const row of rows) {
    const k = (row?.key ?? '').trim()
    if (!k || k === '__proto__') continue // __proto__ 会污染原型,直接丢
    obj[k] = coerceVarValue(row?.value)
  }
  return Object.keys(obj).length ? JSON.stringify(obj) : null
}
