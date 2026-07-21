// React 侧图标 bootstrap:把 4 套离线集注册进 @iconify/react。首屏由 main.tsx 调一次。
// 对应 Vue 侧 web/src/lib/icons.ts 的 setupIcons —— 那边逻辑收敛在 tenon-naive-iconify-picker 包里,
// 本模板**不发第三个包、直接内联**(见台账 C4「不发第三个 npm 包」)。
//
// 每套 icons.json 是独立懒加载 chunk;addCollection 后 <Icon>(AppIcon / 菜单)即可离线解析该 prefix
// 的图标,无需 iconify 在线 API。本地 svg(src/assets/svg)+ 选择器网格留 C4;此处只装菜单/AppIcon
// 渲染所需的 4 个前缀集(ph/lucide/ep/ant-design),与 web/ 一致。
import { addCollection } from '@iconify/react'

// addCollection 的入参类型即 IconifyJSON;经 Parameters 取,免去猜它 re-export 在哪个子包。
type IconifyJSON = Parameters<typeof addCollection>[0]

// 增删默认离线集:装/卸 `@iconify-json/<prefix>`,并在此加/删一行同 prefix 的 loader。
const loaders: Array<() => Promise<IconifyJSON>> = [
  () => import('@iconify-json/ph/icons.json').then((m) => m.default as IconifyJSON),
  () => import('@iconify-json/lucide/icons.json').then((m) => m.default as IconifyJSON),
  () => import('@iconify-json/ep/icons.json').then((m) => m.default as IconifyJSON),
  () => import('@iconify-json/ant-design/icons.json').then((m) => m.default as IconifyJSON),
]

/** 首屏调用一次:异步注册 4 个离线集(非阻塞;<Icon> 会在集合就绪后自动重渲染)。 */
export function setupIcons(): void {
  for (const load of loaders) void load().then(addCollection)
}
