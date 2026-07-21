// **/offline 入口**:零 iconify 在线 API(不请求 api.iconify.design)——本模板可气隙部署,
// 未注册图标只出占位、绝不触网(在线默认入口会 phone home,C3 review HIGH)。symbols 与默认入口一致。
import { Icon } from '@iconify/react/offline'
import type { CSSProperties } from 'react'

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
 */
export function AppIcon({ icon, size = 18, fallback = 'ph:dot-outline-duotone', className, style }: AppIconProps) {
  return <Icon icon={iconName(icon, fallback)} width={size} height={size} className={className} style={style} />
}
