// tenon 侧图标 bootstrap:同步注册常用图标子集，完整离线集由选择器和自定义菜单按需加载。
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
import { _api, addCollection } from '@iconify/vue'
import { lucideCollection, registerCollections, registerLocalIcons, type IconCollection, type IconifyJSON } from 'tenon-naive-iconify-picker'
import coreCollectionsJson from '@/assets/icons.generated.json'

const coreCollections = coreCollectionsJson as unknown as IconifyJSON[]
const coreIconNames = new Set(coreCollections.flatMap((collection) => [
  ...Object.keys(collection.icons).map((name) => `${collection.prefix}:${name}`),
  ...Object.keys(collection.aliases ?? {}).map((name) => `${collection.prefix}:${name}`),
]))
let coreRegistered = false

/** 已同步注册的常用图标可以直接渲染，无需触发完整集合加载。 */
export function isCoreIcon(name: string): boolean {
  return coreRegistered && coreIconNames.has(name)
}

// 增删默认离线集:装/卸 `@iconify-json/<prefix>`,并在此加/删一行同 prefix 的 loader。
// 每套是独立懒加载 chunk；仅选择器或子集外图标需要完整集合。
const collections: IconCollection[] = [
  lucideCollection,
  { prefix: 'ph', name: 'Phosphor', loader: () => import('@iconify-json/ph/icons.json').then((m) => m.default as IconifyJSON) },
  { prefix: 'ep', name: 'Element Plus', loader: () => import('@iconify-json/ep/icons.json').then((m) => m.default as IconifyJSON) },
  { prefix: 'ant-design', name: 'Ant Design', loader: () => import('@iconify-json/ant-design/icons.json').then((m) => m.default as IconifyJSON) },
]

/** 首屏调用一次：同步注册常用子集，再配置完整离线集和本地 SVG。 */
export function setupIcons(): void {
  // 布局和登录页如果直接用了在线 Icon，缺图标时会打 api.iconify.design / simplesvg / unisvg。
  // 内网打不开这些主机。这里换成必失败的本地响应，图标只走已打包的集合。
  _api.setFetch(() => Promise.resolve(new Response('', { status: 404 })))
  if (!coreRegistered) {
    for (const collection of coreCollections) addCollection(collection)
    coreRegistered = true
  }
  registerCollections(collections)
  registerLocalIcons(import.meta.glob('/src/assets/svg/*.svg', { query: '?raw', import: 'default', eager: true }) as Record<string, string>)
}
