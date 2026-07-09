// 离线图标注册 + 本地 SVG 枚举。
// 参照 XiHan(同栈 Naive UI)packages/iconify/offline.ts:内置 @iconify-json/* 按需 addCollection,
// 已注册的集由 @iconify/vue <Icon> 从本地数据渲染(不请求外部 CDN);未内置前缀走在线兜底。
import { addCollection } from '@iconify/vue'

type IconifyJSON = Parameters<typeof addCollection>[0]

export interface IconSetMeta {
  prefix: string
  name: string
}

/**
 * 内置离线图标集 —— 单一配置源。选择器 Tab、离线注册、渲染全部由这里驱动。
 *
 * ▸ 增删一套默认图标集,改三处即可:
 *   1) 依赖:`npm i -D @iconify-json/<prefix>`(装=打包进离线;`npm un @iconify-json/<prefix>`=移除)
 *   2) 本数组 ICON_SET_META:加/删一项 `{ prefix, name }`(name 是 Tab 显示名)
 *   3) 下方 LOADERS:加/删一行 `<prefix>: () => import('@iconify-json/<prefix>/icons.json')...`
 *
 * ▸ 每套都是独立懒加载 chunk(不进主包),体积即 dist 增量;原始/gzip 参考:
 *   ph 4.4M/946K · mdi 2.9M/725K · tabler 2.1M/336K · ant-design 688K/168K · lucide 559K/85K · ep 111K/31K
 * ▸ `ph` 是全站默认集且被 preloadRuntimeIcons 预热,勿移除。未内置的集仍可在选择器「在线」页联网使用。
 */
export const ICON_SET_META: IconSetMeta[] = [
  { prefix: 'ph', name: 'Phosphor' },
  { prefix: 'lucide', name: 'Lucide' },
  { prefix: 'ep', name: 'Element Plus' },
  { prefix: 'ant-design', name: 'Ant Design' },
]

/** 本地自定义 SVG 前缀,值格式 `local:<文件名>`。 */
export const LOCAL_PREFIX = 'local'

// 动态 import 的 icons.json → 每套独立 chunk,由应用自身源提供(离线即“不依赖外部 iconify CDN”)。
// 每加一套 ICON_SET_META,这里补一行同 prefix 的 loader(见上方说明)。
const LOADERS: Record<string, () => Promise<IconifyJSON>> = {
  ph: () => import('@iconify-json/ph/icons.json').then((m) => m.default as IconifyJSON),
  lucide: () => import('@iconify-json/lucide/icons.json').then((m) => m.default as IconifyJSON),
  ep: () => import('@iconify-json/ep/icons.json').then((m) => m.default as IconifyJSON),
  'ant-design': () => import('@iconify-json/ant-design/icons.json').then((m) => m.default as IconifyJSON),
}

const dataCache: Record<string, IconifyJSON> = {}
const registered = new Set<string>()

/** 内置前缀:动态 import + addCollection(幂等),返回 JSON。非内置:返回 null(交在线兜底)。 */
export async function ensureCollection(prefix: string): Promise<IconifyJSON | null> {
  if (dataCache[prefix]) return dataCache[prefix]
  const loader = LOADERS[prefix]
  if (!loader) return null
  const data = await loader().catch(() => null)
  if (!data) return null
  if (!registered.has(prefix)) {
    addCollection(data)
    registered.add(prefix)
  }
  dataCache[prefix] = data
  return data
}

/** 该前缀是否为内置离线集(决定是否需等注册再渲染,避免命中外部 CDN)。 */
export function isBundled(prefix: string): boolean {
  return prefix in LOADERS
}

/** 该前缀是否已完成离线注册。 */
export function isRegistered(prefix: string): boolean {
  return registered.has(prefix)
}

/** 首屏预热:tenon 全站用 ph,提前拉起离线注册(非阻塞;AppIcon 会等注册完再渲)。 */
export async function preloadRuntimeIcons(): Promise<void> {
  await ensureCollection('ph')
}

/** 懒加载并返回排序后的图标名(供 IconPicker 列名)。 */
export async function loadIconNames(prefix: string): Promise<string[]> {
  const data = await ensureCollection(prefix)
  return data ? Object.keys(data.icons ?? {}).sort() : []
}

// 本地自定义 SVG:丢 .svg 进 src/assets/svg/ 即离线可用(Vite 原生 glob raw,零构建插件)。
const localRawMap: Record<string, string> = {}
for (const [path, raw] of Object.entries(
  import.meta.glob('/src/assets/svg/*.svg', { query: '?raw', import: 'default', eager: true }) as Record<string, string>,
)) {
  const name = path.split('/').pop()?.replace('.svg', '') ?? ''
  if (name) localRawMap[name] = raw
}

/** 本地 SVG 名列表(不含 `local:` 前缀),供 IconPicker 本地页。 */
export const localIconNames: string[] = Object.keys(localRawMap).sort()

/** 取本地 SVG 原始字符串(未命中返回 undefined)。 */
export function localSvgRaw(name: string): string | undefined {
  return localRawMap[name]
}
