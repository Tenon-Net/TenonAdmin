// User-Agent 轻量解析:原始 UA 串 → 浏览器 / 操作系统摘要,供日志/会话列表展示。
// ponytail: 正则启发式,只认主流浏览器+系统,够后台"一眼分辨设备"用;要精确设备指纹再引 ua-parser-js。
//           刻意放前端——后端内核只存原始 UA、不引解析依赖(核心包只依赖 SqlSugar + Microsoft.*)。

/** 顺序敏感:Edge 含 Chrome 标记、Opera 含 Chrome 标记,故派生浏览器要排在 Chrome 前。 */
const BROWSERS: [RegExp, string][] = [
  [/Edg(?:e|A|iOS)?\//, 'Edge'],
  [/OPR\/|Opera/, 'Opera'],
  [/Firefox\/|FxiOS/, 'Firefox'],
  [/Chrome\/|CriOS/, 'Chrome'],
  [/Safari\//, 'Safari'],
  [/MSIE |Trident\//, 'IE'],
]

const SYSTEMS: [RegExp, string][] = [
  [/Windows NT 10/, 'Windows 10/11'],
  [/Windows NT 6\.3/, 'Windows 8.1'],
  [/Windows NT 6\.[12]/, 'Windows 7/8'],
  [/Windows/, 'Windows'],
  [/iPhone|iPad|iPod/, 'iOS'],
  [/Mac OS X|Macintosh/, 'macOS'],
  [/Android/, 'Android'],
  [/Linux/, 'Linux'],
]

function match(ua: string, table: [RegExp, string][]): string {
  for (const [re, name] of table) if (re.test(ua)) return name
  return ''
}

/** 解析 UA。空串/未知返回空字段,由调用方决定占位符。 */
export function parseUserAgent(ua?: string | null): { browser: string; os: string } {
  if (!ua) return { browser: '', os: '' }
  return { browser: match(ua, BROWSERS), os: match(ua, SYSTEMS) }
}

/** 单行摘要,如 "Chrome · Windows 10/11";全未识别回落原始串,再空则 "—"。 */
export function uaSummary(ua?: string | null): string {
  const { browser, os } = parseUserAgent(ua)
  const parts = [browser, os].filter(Boolean)
  return parts.length ? parts.join(' · ') : ua || '—'
}
