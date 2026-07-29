// 定时任务表单纯逻辑(变异钉):行 ↔ 表单值映射、属性包键值对组装、触发描述。
// 属性包在表单里是键值对 UI,提交时组装成 properties 对象;HTTP 的 headers 子键值对
// 序列化成 JSON 字符串放 properties.headers(后端契约,scheduling-ledger §7)。
// HTTP headers 值在读取时被后端掩码成 "********",原样回传即"不改" —— 所以往返必须无损。
import dayjs, { type Dayjs } from 'dayjs'
import type { JobInput, SysJob } from '@/types/api'

export interface KvPair {
  key: string
  value: string
}

/** 表单态(与 JobInput 的差异:属性包拆成键值对、时刻是 Dayjs、HTTP/SQL 载荷平铺成专字段)。 */
export interface JobFormValues {
  code: string
  name: string
  remark: string
  triggerKind: number
  cronExpression: string
  intervalSeconds: number | null
  oneShotTime: Dayjs | null
  startTime: Dayjs | null
  endTime: Dayjs | null
  misfireStrategy: number
  concurrencyMode: number
  handlerKind: number
  handlerName: string
  /** 编译类自定义属性包(键值对) */
  props: KvPair[]
  httpUrl: string
  httpMethod: string
  /** HTTP 请求头(键值对;值可能是掩码 "********",不动即"不改") */
  httpHeaders: KvPair[]
  httpBody: string
  httpSuccessStatuses: string
  sql: string
  timeoutSeconds: number
  retryCount: number
  retryIntervalSeconds: number
  failAlertThreshold: number
  alertByNotice: boolean
  alertEmails: string
}

/** propsJson 安全解析(null 值收成空串;坏 JSON 给空对象,不炸表单)。 */
export function parsePropsJson(propsJson?: string | null): Record<string, string> {
  if (!propsJson) return {}
  try {
    const raw = JSON.parse(propsJson) as Record<string, string | null>
    const out: Record<string, string> = {}
    for (const [k, v] of Object.entries(raw)) out[k] = v ?? ''
    return out
  } catch {
    return {}
  }
}

/** 对象 → 键值对数组(表单 Form.List 消费)。 */
export function propsToPairs(props: Record<string, string>): KvPair[] {
  return Object.entries(props).map(([key, value]) => ({ key, value }))
}

/** 键值对数组 → 对象;空键行丢弃(用户加了行没填),同键后者覆盖前者。 */
export function pairsToProps(pairs: KvPair[]): Record<string, string> {
  const out: Record<string, string> = {}
  for (const p of pairs ?? []) {
    const k = (p?.key ?? '').trim()
    if (k) out[k] = p.value ?? ''
  }
  return out
}

/** headers JSON 串 → 键值对(掩码值原样进 UI;坏 JSON 给空 —— 只会出现在绕过本 UI 写库的数据上)。 */
export function parseHeaders(headersJson?: string): KvPair[] {
  if (!headersJson) return []
  try {
    const raw = JSON.parse(headersJson) as Record<string, string | null>
    if (raw === null || typeof raw !== 'object' || Array.isArray(raw)) return []
    return Object.entries(raw).map(([key, value]) => ({ key, value: value ?? '' }))
  } catch {
    return []
  }
}

const fmt = (d: Dayjs | null): string | null => (d ? d.format('YYYY-MM-DDTHH:mm:ss') : null)

/** 新增缺省(cron 触发 + 编译类载荷;失败处理全零 = 不重试不告警)。 */
export function blankJob(): JobFormValues {
  return {
    code: '',
    name: '',
    remark: '',
    triggerKind: 1,
    cronExpression: '0 0 0 * * ?',
    intervalSeconds: 60,
    oneShotTime: null,
    startTime: null,
    endTime: null,
    misfireStrategy: 1,
    concurrencyMode: 1,
    handlerKind: 1,
    handlerName: '',
    props: [],
    httpUrl: '',
    httpMethod: 'GET',
    httpHeaders: [],
    httpBody: '',
    httpSuccessStatuses: '',
    sql: '',
    timeoutSeconds: 0,
    retryCount: 0,
    retryIntervalSeconds: 30,
    failAlertThreshold: 0,
    // 站内信告警跟后端保持一致地默认开:SysJob.AlertByNotice 与 JobInput.AlertByNotice 都是 true。
    // 这里曾是 false —— 等于从 React 建的任务默认收不到告警,而同样的操作在 Vue 侧是收得到的。
    alertByNotice: true,
    alertEmails: '',
  }
}

/**
 * 该行动过任一「高级选项」?动过就默认展开那一块,免得用户觉得"我配过的东西不见了"。
 * 判据是"与新增缺省不同",所以缺省值改了这里自动跟着走。
 */
export function hasAdvanced(r: SysJob): boolean {
  const d = blankJob()
  return !!r.startTime || !!r.endTime
    || r.misfireStrategy !== d.misfireStrategy
    || r.concurrencyMode !== d.concurrencyMode
    || r.timeoutSeconds !== d.timeoutSeconds
    || r.retryCount !== d.retryCount
    || r.retryIntervalSeconds !== d.retryIntervalSeconds
    || r.failAlertThreshold !== d.failAlertThreshold
    || r.alertByNotice !== d.alertByNotice
    || !!r.alertEmails
}

/** 行数据 → 表单值(page 行含全列,编辑不用另拉详情)。 */
export function rowToForm(r: SysJob): JobFormValues {
  const props = parsePropsJson(r.propsJson)
  const isHttp = r.handlerKind === 2
  const isSql = r.handlerKind === 3
  return {
    ...blankJob(),
    code: r.code,
    name: r.name,
    remark: r.remark ?? '',
    triggerKind: r.triggerKind,
    cronExpression: r.cronExpression ?? '',
    intervalSeconds: r.intervalSeconds ?? null,
    oneShotTime: r.oneShotTime ? dayjs(r.oneShotTime) : null,
    startTime: r.startTime ? dayjs(r.startTime) : null,
    endTime: r.endTime ? dayjs(r.endTime) : null,
    misfireStrategy: r.misfireStrategy,
    concurrencyMode: r.concurrencyMode,
    handlerKind: r.handlerKind,
    handlerName: r.handlerKind === 1 ? r.handlerName : '',
    props: r.handlerKind === 1 ? propsToPairs(props) : [],
    httpUrl: isHttp ? (props.url ?? '') : '',
    httpMethod: isHttp ? (props.method || 'GET') : 'GET',
    httpHeaders: isHttp ? parseHeaders(props.headers) : [],
    httpBody: isHttp ? (props.body ?? '') : '',
    httpSuccessStatuses: isHttp ? (props.successStatuses ?? '') : '',
    sql: isSql ? (props.sql ?? '') : '',
    timeoutSeconds: r.timeoutSeconds,
    retryCount: r.retryCount,
    retryIntervalSeconds: r.retryIntervalSeconds,
    failAlertThreshold: r.failAlertThreshold,
    alertByNotice: r.alertByNotice,
    alertEmails: r.alertEmails ?? '',
  }
}

/** 表单值 → 提交入参:按载荷类型组装 properties,按触发类型只带相关触发字段(其余置 null,防脏残留)。 */
export function formToInput(v: JobFormValues): JobInput {
  let properties: Record<string, string> = {}
  if (v.handlerKind === 1) {
    properties = pairsToProps(v.props)
  } else if (v.handlerKind === 2) {
    properties.url = v.httpUrl.trim()
    if (v.httpMethod && v.httpMethod !== 'GET') properties.method = v.httpMethod
    const headers = pairsToProps(v.httpHeaders)
    if (Object.keys(headers).length > 0) properties.headers = JSON.stringify(headers)
    if (v.httpBody.trim()) properties.body = v.httpBody
    if (v.httpSuccessStatuses.trim()) properties.successStatuses = v.httpSuccessStatuses.trim()
  } else if (v.handlerKind === 3) {
    properties.sql = v.sql
  }
  return {
    code: v.code.trim() || undefined,
    name: v.name.trim(),
    handlerKind: v.handlerKind,
    // 编译类填 IAdminJob.Name;HTTP/SQL 传什么都被服务端覆盖成内置处理器名,不传
    handlerName: v.handlerKind === 1 ? v.handlerName : undefined,
    properties,
    triggerKind: v.triggerKind,
    cronExpression: v.triggerKind === 1 ? v.cronExpression.trim() : null,
    intervalSeconds: v.triggerKind === 2 ? v.intervalSeconds : null,
    oneShotTime: v.triggerKind === 3 ? fmt(v.oneShotTime) : null,
    startTime: fmt(v.startTime),
    endTime: fmt(v.endTime),
    misfireStrategy: v.misfireStrategy,
    concurrencyMode: v.concurrencyMode,
    timeoutSeconds: v.timeoutSeconds,
    retryCount: v.retryCount,
    retryIntervalSeconds: v.retryIntervalSeconds,
    failAlertThreshold: v.failAlertThreshold,
    alertByNotice: v.alertByNotice,
    alertEmails: v.alertEmails.trim() || null,
    remark: v.remark.trim() || null,
  }
}

/** 触发描述列:cron 原文 /「每 N 秒」/ 一次性时刻(t 由页面注入,纯函数便于测)。 */
export function describeTrigger(
  r: Pick<SysJob, 'triggerKind' | 'cronExpression' | 'intervalSeconds' | 'oneShotTime'>,
  t: (key: string, args?: Record<string, unknown>) => string,
): string {
  if (r.triggerKind === 1) return r.cronExpression ?? ''
  if (r.triggerKind === 2) return t('job.trigger.every', { n: r.intervalSeconds ?? 0 })
  if (r.triggerKind === 3) return t('job.trigger.at', { time: (r.oneShotTime ?? '').replace('T', ' ').slice(0, 19) })
  return ''
}
