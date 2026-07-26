// CronEditor 纯逻辑:6 段秒级 cron 的段级解析/组装(与后端 CronExpression 语法对齐,scheduling-ledger §4)。
// 只认数字形态(JAN/SUN 等名字不进可视化编辑,归"自定义片段"走表达式页签);周 0=周日(后端 7 等价 0)。
// 与渲染解耦,变异钉在 cronParts.spec.ts。

/** 段下标:0 秒 1 分 2 时 3 日 4 月 5 周。 */
export type SegIndex = 0 | 1 | 2 | 3 | 4 | 5

/** 各段取值范围(周 0-6,0=周日;后端把 7 归一成 0)。 */
export const SEG_RANGES: ReadonlyArray<readonly [number, number]> = [
  [0, 59], // 秒
  [0, 59], // 分
  [0, 23], // 时
  [1, 31], // 日
  [1, 12], // 月
  [0, 6], // 周
]

/** 段状态(可视化编辑器认识的全部形态;解析不进任何一种 = 自定义片段,UI 只显原文)。 */
export type SegState =
  | { mode: 'every' } // *
  | { mode: 'unspecified' } // ?(仅日/周)
  | { mode: 'range'; from: number; to: number } // a-b
  | { mode: 'step'; from: number | null; step: number } // */n 或 a/n(from=null 即 *)
  | { mode: 'values'; values: number[] } // a,b,c(单值也归这里)
  | { mode: 'lastDay' } // L(日)
  | { mode: 'lastOffset'; n: number } // L-n(日)
  | { mode: 'lastWeekday' } // LW(日)
  | { mode: 'nearestWeekday'; n: number } // nW(日)
  | { mode: 'lastDow'; dow: number } // nL(周)
  | { mode: 'nthDow'; dow: number; nth: number } // n#m(周)

/** 拆表达式:6 段原样;5 段(分 时 日 月 周)升 6 段(秒位补 0);空串给默认;其余段数返回 null(交表达式页签)。 */
export function splitCron(expr: string): string[] | null {
  const tokens = expr.trim().split(/\s+/).filter(Boolean)
  if (tokens.length === 0) return ['0', '*', '*', '*', '*', '?']
  if (tokens.length === 6) return tokens
  if (tokens.length === 5) return ['0', ...tokens]
  return null
}

/** 拼表达式(6 段空格连接)。 */
export function joinCron(segs: string[]): string {
  return segs.join(' ')
}

/** 该段是否"受限"(非 * / ?)——日与周不能同时受限(后端 47003)。 */
export function isRestricted(seg: string): boolean {
  return seg !== '*' && seg !== '?'
}

const INT = /^\d+$/

function inRange(n: number, idx: SegIndex): boolean {
  const [min, max] = SEG_RANGES[idx]!
  return n >= min && n <= max
}

/**
 * 周段单值:只认 0-7(7 折成 0=周日,后端 ParseDowValue 同款),其余返回 null。
 * 不能拿 `% 7` 兜底——那会把后端必拒的 `8L` 悄悄读成"周一最后一个",
 * 用户看到的控件与自己写的表达式不是一回事。
 */
function normalizeDow(raw: string): number | null {
  const n = Number(raw)
  return n >= 0 && n <= 7 ? n % 7 : null
}

/**
 * 解析单段文本 → 段状态;不认识(名字、L/W 混枚举、越界、混合列表如 `1-5,10`)返回 null = 自定义片段。
 * 专项(L/L-n/nW/LW/nL/n#m)只在对应段位(日=3/周=5)成立,别的段位一律 null。
 */
export function parseSegment(text: string, idx: SegIndex): SegState | null {
  const s = text.trim().toUpperCase()
  if (s === '') return null
  if (s === '*') return { mode: 'every' }
  if (s === '?') return idx === 3 || idx === 5 ? { mode: 'unspecified' } : null

  // 日段专项
  if (idx === 3) {
    if (s === 'L') return { mode: 'lastDay' }
    if (s === 'LW') return { mode: 'lastWeekday' }
    const lastOffset = /^L-(\d+)$/.exec(s)
    if (lastOffset) return { mode: 'lastOffset', n: Number(lastOffset[1]) }
    const nearest = /^(\d+)W$/.exec(s)
    if (nearest) {
      const n = Number(nearest[1])
      return inRange(n, 3) ? { mode: 'nearestWeekday', n } : null
    }
  }
  // 周段专项(孤立 L=SAT 是 Quartz 冷知识,不进可视化;nL / n#m 才是)
  if (idx === 5) {
    const lastDow = /^(\d)L$/.exec(s)
    if (lastDow) {
      const dow = normalizeDow(lastDow[1])
      return dow === null ? null : { mode: 'lastDow', dow }
    }
    const nth = /^(\d)#(\d)$/.exec(s)
    if (nth) {
      const dow = normalizeDow(nth[1])
      const m = Number(nth[2])
      return dow !== null && m >= 1 && m <= 5 ? { mode: 'nthDow', dow, nth: m } : null
    }
  }

  // 步长 */n 或 a/n
  const step = /^(\*|\d+)\/(\d+)$/.exec(s)
  if (step) {
    const n = Number(step[2])
    if (n < 1) return null
    if (step[1] === '*') return { mode: 'step', from: null, step: n }
    const from = Number(step[1])
    return inRange(from, idx) ? { mode: 'step', from, step: n } : null
  }
  // 区间 a-b(环绕如周 5-1 后端合法,可视化照收)
  const range = /^(\d+)-(\d+)$/.exec(s)
  if (range) {
    const from = Number(range[1])
    const to = Number(range[2])
    return inRange(from, idx) && inRange(to, idx) ? { mode: 'range', from, to } : null
  }
  // 指定值 a,b,c(单值也算)
  if (s.split(',').every((p) => INT.test(p))) {
    const values = s.split(',').map(Number)
    if (values.every((v) => inRange(v, idx))) return { mode: 'values', values }
  }
  return null
}

/** 段状态 → 段文本(compose(parse(x)) 对可识别形态稳定;values 为空退 *,防拼出空段)。 */
export function composeSegment(state: SegState): string {
  switch (state.mode) {
    case 'every':
      return '*'
    case 'unspecified':
      return '?'
    case 'range':
      return `${state.from}-${state.to}`
    case 'step':
      return `${state.from ?? '*'}/${state.step}`
    case 'values':
      return state.values.length ? [...state.values].sort((a, b) => a - b).join(',') : '*'
    case 'lastDay':
      return 'L'
    case 'lastOffset':
      return `L-${state.n}`
    case 'lastWeekday':
      return 'LW'
    case 'nearestWeekday':
      return `${state.n}W`
    case 'lastDow':
      return `${state.dow}L`
    case 'nthDow':
      return `${state.dow}#${state.nth}`
  }
}

/**
 * 写入段并维护日/周互斥:一侧受限时,另一侧自动落 `?`(后端日周同限直接拒 47003)。
 * 返回新数组,不改入参。
 */
export function setSegment(segs: string[], idx: SegIndex, text: string): string[] {
  const next = [...segs]
  next[idx] = text
  if (idx === 3 && isRestricted(text) && next[5] !== '?') next[5] = '?'
  if (idx === 5 && isRestricted(text) && next[3] !== '?') next[3] = '?'
  // 两侧都不受限时避免双 ?(后端把 ? 当 * 处理,但双 ? 读着别扭):留一侧 ? 即可,这里不强改。
  return next
}
