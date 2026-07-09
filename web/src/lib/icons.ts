// tenon 侧图标 bootstrap:把 4 套离线集 + 本地 SVG 注册进 tenon-naive-iconify-picker,首屏预热 ph。
// 选择器/渲染器逻辑已收敛到该 npm 包(见 @/components/IconPicker、@/components/AppIcon),这里只做「注册配置」。
//
// ┌── 图标组件有 bug 时怎么改(源头在包,不在 tenon)─────────────────────────────┐
// │ 组件本体 = 已发布的 npm 包 tenon-naive-iconify-picker(github.com/Tenon-Net/tenon-naive-iconify-picker)。
// │ tenon 只保留三个薄封装(本文件 + AppIcon.vue + IconPicker/index.vue),切勿在 tenon 里
// │ 复制粘贴修组件逻辑 —— 那会让重复回来。标准回路:
// │   1) 本地实时改:web/ 下 `NIP_LOCAL=1 npm run dev`。vite.config.ts 的门控别名会把
// │      'tenon-naive-iconify-picker' 直连兄弟仓库 ../../tenon-naive-iconify-picker/src,
// │      改包 src/*.vue 在 tenon 里 HMR 秒见(免 build/发版);顺便在包自带 playground 也验一遍。
// │   2) 发补丁版:在包仓库 `npm version patch && git push && git push --tags`
// │      → GitHub Actions Trusted Publishing 经 OIDC 自动发布(零密钥、自带 provenance)。
// │   3) tenon 升级:`npm i tenon-naive-iconify-picker@latest`,取消 NIP_LOCAL,对真实 dist 冒烟后提交。
// └──────────────────────────────────────────────────────────────────────────────┘
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
