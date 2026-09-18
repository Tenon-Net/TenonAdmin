// 发起页的纯逻辑(变异钉):摘要变量的键值行 → variablesJson。与 Vue 侧 start/index.vue 同规则。
export interface WfVarRow {
  key?: string
  value?: string
}

/** 十进制文本规范形：去整数前导零、去小数尾零，并把所有负零归一为 0。 */
function canonicalDecimal(value: string): string {
  const negative = value.startsWith('-')
  const unsigned = negative ? value.slice(1) : value
  const [integerPart, fractionPart] = unsigned.split('.')
  const integer = integerPart!.replace(/^0+(?=\d)/, '')
  const fraction = fractionPart?.replace(/0+$/, '') ?? ''
  const magnitude = fraction ? `${integer}.${fraction}` : integer
  return negative && magnitude !== '0' ? `-${magnitude}` : magnitude
}

/** 'true'/'false' → 布尔；仅当 JSON number 的可见文本无精度损失时才转数字。 */
export function coerceVarValue(raw: string | undefined): unknown {
  const s = (raw ?? '').trim()
  if (s === '') return ''
  if (s === 'true') return true
  if (s === 'false') return false
  if (/^-?\d+(?:\.\d+)?$/.test(s)) {
    const canonical = canonicalDecimal(s)
    const value = Number(s)
    if (!Number.isFinite(value) || String(value) !== canonical) return raw
    if (canonical === '0') return 0
    return canonical.includes('.') || Number.isSafeInteger(value) ? value : raw
  }
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
