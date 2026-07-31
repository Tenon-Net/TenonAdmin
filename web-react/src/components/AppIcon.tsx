// **/offline 入口**:零 iconify 在线 API(不请求 api.iconify.design)——本模板可气隙部署,
// 未注册图标只出占位、绝不触网(在线默认入口会 phone home,C3 review HIGH)。symbols 与默认入口一致。
import { Icon } from '@iconify/react/offline'
import type { CSSProperties } from 'react'
import { useEffect, useState } from 'react'
import { ensureIconLoaded } from '@/lib/icons'

/**
 * iconify 图标名兜底:空串 / 未传 → fallback。与 Vue 侧 `renderIcon` 的 `name || fallback` 同义。
 * **用 `||` 而非 `??`**:后端可能存了空串 icon(不只是 null),空串也要退兜底;`??` 只兜 null/undefined,
 * 会把 `''` 当有效图标传给 <Icon> → 渲染空白。
 */
export function iconName(icon: string | undefined, fallback: string): string {
  return icon || fallback
}

export interface AppIconProps {
  icon?: string
  size?: number | string
  /** 未传/空时的兜底图标。菜单目录用 `ph:folder-duotone`,叶子/通用用默认 `ph:dot-outline-duotone`。 */
  fallback?: string
  className?: string
  style?: CSSProperties
}

/**
 * tenon 图标渲染器:@iconify/react `<Icon>` 薄封装 + 空值兜底。离线集由 `lib/icons.setupIcons` 注册,
 * 故渲染后端配的任意 iconify 串(`ph:*`/`lucide:*`/`ep:*`/`ant-design:*`)全离线、不打在线 API。
 * 对应 Vue 侧 `AppIcon.vue`(那边包 tenon-naive-iconify-picker 的 OfflineIcon)。
 *
 * 首屏只带 `icons.generated.json` 小子集;菜单种子里像 `ph:timer-duotone` 这类不在子集的图标,
 * 要经 `ensureIconLoaded` 懒加载完整前缀包。**加载完成必须 bump 一次重渲染**——`@iconify/react/offline`
 * 在 addCollection 后不会自动叫醒已挂载的 `<Icon>`,否则侧栏会出现"空白,点开目录才出图标"。
 */
export function AppIcon({ icon, size = 18, fallback = 'ph:dot-outline-duotone', className, style }: AppIconProps) {
  const name = iconName(icon, fallback)
  // 仅作"集合已就绪"的重渲染扳机,不参与 DOM 属性。
  const [, setReadyTick] = useState(0)
  useEffect(() => {
    let live = true
    // Promise.resolve:测试 mock 若漏 return Promise,也不要把 .then 打到 undefined 上。
    void Promise.resolve(ensureIconLoaded(name)).then(() => {
      if (live) setReadyTick((n) => n + 1)
    })
    return () => {
      live = false
    }
  }, [name])
  return <Icon icon={name} width={size} height={size} className={className} style={style} />
}
