// tenon 侧图标 bootstrap:把 4 套离线集 + 本地 SVG 注册进 tenon-naive-iconify-picker,首屏预热 ph。
// 选择器/渲染器逻辑已收敛到该 npm 包(见 @/components/IconPicker、@/components/AppIcon),这里只做「注册配置」。
import { setupIconPicker, type IconCollection, type IconifyJSON } from 'tenon-naive-iconify-picker'

// 增删默认离线集:装/卸 `@iconify-json/<prefix>`,并在此加/删一行同 prefix 的 loader。
// 每套是独立懒加载 chunk;`ph` 是全站默认集且被预热,勿删。
const collections: IconCollection[] = [
  { prefix: 'ph', name: 'Phosphor', loader: () => import('@iconify-json/ph/icons.json').then((m) => m.default as IconifyJSON) },
  { prefix: 'lucide', name: 'Lucide', loader: () => import('@iconify-json/lucide/icons.json').then((m) => m.default as IconifyJSON) },
  { prefix: 'ep', name: 'Element Plus', loader: () => import('@iconify-json/ep/icons.json').then((m) => m.default as IconifyJSON) },
  { prefix: 'ant-design', name: 'Ant Design', loader: () => import('@iconify-json/ant-design/icons.json').then((m) => m.default as IconifyJSON) },
]

/** 首屏调用一次:注册离线集 + 本地 SVG(src/assets/svg/*.svg),预热 ph(非阻塞)。 */
export function setupIcons(): void {
  setupIconPicker({
    collections,
    localIcons: import.meta.glob('/src/assets/svg/*.svg', { query: '?raw', import: 'default', eager: true }) as Record<string, string>,
    preloadPrefix: 'ph',
  })
}
