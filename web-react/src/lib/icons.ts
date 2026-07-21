// React 侧图标 bootstrap:注册 4 套离线集 + 本地 svg 进 @iconify/react/**offline**(零在线 API,可气隙)。
// 首屏由 main.tsx 调 setupIcons() 一次;IconPicker(C4)另经 loadIconNames / getLocalIconNames 枚举图标名。
// 对应 Vue 侧 web/src/lib/icons.ts + tenon-naive-iconify-picker/src/icons.ts —— 本模板不发包、直接内联。
//
// **/offline 入口**:未注册图标只出占位、绝不请求 api.iconify.design(C3 review HIGH)。symbols 与默认入口一致。
import { addCollection, addIcon } from '@iconify/react/offline'

// addCollection/addIcon 的入参类型即 IconifyJSON / IconifyIcon;经 Parameters 取,免猜它 re-export 在哪个子包。
type IconifyJSON = Parameters<typeof addCollection>[0]
type IconifyIcon = Parameters<typeof addIcon>[1]

/** 本地自定义 svg 前缀,值格式 `local:<名字>`。 */
export const LOCAL_PREFIX = 'local'

/** 选择器 Tab 用的集合元数据(顺序即 Tab 顺序;ph 为首/默认页)。 */
export interface CollectionMeta {
  prefix: string
  name: string
}
export const COLLECTIONS: CollectionMeta[] = [
  { prefix: 'ph', name: 'Phosphor' },
  { prefix: 'lucide', name: 'Lucide' },
  { prefix: 'ep', name: 'Element Plus' },
  { prefix: 'ant-design', name: 'Ant Design' },
]

// 每套 icons.json 独立懒加载 chunk;增删离线集:装/卸 `@iconify-json/<prefix>`,并在此加/删一行同 prefix 的 loader。
const loaders: Record<string, () => Promise<IconifyJSON>> = {
  ph: () => import('@iconify-json/ph/icons.json').then((m) => m.default as IconifyJSON),
  lucide: () => import('@iconify-json/lucide/icons.json').then((m) => m.default as IconifyJSON),
  ep: () => import('@iconify-json/ep/icons.json').then((m) => m.default as IconifyJSON),
  'ant-design': () => import('@iconify-json/ant-design/icons.json').then((m) => m.default as IconifyJSON),
}

const nameCache: Record<string, string[]> = {}

/** 该前缀是否为内置离线集(对齐 Vue 包的 isBundled)。COLLECTIONS ↔ loaders 一致性靠它可测:
 *  某 Tab 前缀若无对应 loader,其网格会静默空,靠 `COLLECTIONS.every(isBundled)` 钉住。 */
export function isBundled(prefix: string): boolean {
  return prefix in loaders
}

/**
 * 懒加载某内置集:`addCollection`(供渲染)+ 返回**排序后**的图标名(供选择器网格),按前缀缓存。
 * 未知前缀返回空数组。与 setupIcons 的 addCollection 幂等叠加(import() 走同一缓存,JSON 只下一次)。
 */
export async function loadIconNames(prefix: string): Promise<string[]> {
  if (nameCache[prefix]) return nameCache[prefix]
  const load = loaders[prefix]
  if (!load) return []
  const data = await load()
  addCollection(data)
  const names = sortedIconNames(data)
  nameCache[prefix] = names
  return names
}

/** 集合 JSON → 排序后的图标名(纯逻辑,便于单测;`?? {}` 兜住无 icons 的畸形集)。 */
export function sortedIconNames(data: IconifyJSON): string[] {
  return Object.keys(data.icons ?? {}).sort()
}

/** 原始 `<svg>` 串 → iconify 图标数据:取 viewBox 的原点+宽高 + 剥掉外层 `<svg>` 的内容体。 */
export function svgToIcon(raw: string): IconifyIcon {
  const vb = raw.match(/viewBox=["']([\d.\s-]+)["']/)?.[1]?.trim().split(/\s+/).map(Number)
  const ok = vb?.length === 4 && vb.every((n) => !Number.isNaN(n))
  // viewBox 四段 = [min-x, min-y, width, height];left/top 承接非零原点(iconify 支持,默认 0),
  // 否则 viewBox="-2 -2 24 24" 这类会渲染偏移/裁切(C4 review LOW)。
  const [left, top, width, height] = ok ? (vb as number[]) : [0, 0, 24, 24]
  const body = raw.replace(/<svg[^>]*>/i, '').replace(/<\/svg>\s*$/i, '').trim()
  return { body, left, top, width, height }
}

// 本地 svg:src/assets/svg/*.svg,eager raw 导入 → 模块级注册成 `local:<名字>`(供 AppIcon 渲染 + 选择器 local tab)。
const localModules = import.meta.glob('/src/assets/svg/*.svg', { query: '?raw', import: 'default', eager: true }) as Record<string, string>
const localNames: string[] = Object.entries(localModules)
  .map(([path, raw]) => {
    const name = path.split('/').pop()!.replace(/\.svg$/, '')
    addIcon(`${LOCAL_PREFIX}:${name}`, svgToIcon(raw))
    return name
  })
  .sort()

/** 本地 svg 名列表(不含 `local:` 前缀;供选择器 local tab)。 */
export function getLocalIconNames(): string[] {
  return localNames
}

/** 首屏调用一次:异步注册 4 个离线集(非阻塞;<Icon> 在集合就绪后自动重渲染)。本地 svg 已在模块级注册。 */
export function setupIcons(): void {
  for (const prefix in loaders) void loaders[prefix]().then(addCollection)
}
