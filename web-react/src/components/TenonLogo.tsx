import { useAppStore, isDark } from '@/stores/app'

/**
 * 品牌配色(明暗两版)。抽出成纯函数便于变异钉死。**固定品牌身份色 #646CFF,不随 accent 变**。
 * 值源自 design-mockups/brand/tenon-logo(-dark).svg,与 Vue 侧 TenonLogo.vue 逐字一致。
 */
export function logoColors(dark: boolean): { bg: string; mark: string; markOpacity: number } {
  return dark
    ? { bg: '#16181D', mark: '#7A81FF', markOpacity: 0.45 }
    : { bg: '#646CFF', mark: '#FFFFFF', markOpacity: 0.5 }
}

/** Tenon 品牌徽标(透榫)。内联 SVG,明暗跟随 app store。对应 Vue 侧 `TenonLogo.vue`。 */
export function TenonLogo({ size = 28 }: { size?: number }) {
  const dark = useAppStore(isDark)
  const { bg, mark, markOpacity } = logoColors(dark)
  return (
    <svg width={size} height={size} viewBox="0 0 120 120" role="img" aria-label="Tenon">
      <rect width="120" height="120" rx="26" fill={bg} />
      <g transform="translate(21,21) scale(0.65)">
        <rect x="16" y="27" width="88" height="26" rx="6" fill={mark} fillOpacity={markOpacity} />
        <rect x="47" y="27" width="26" height="76" rx="6" fill={mark} />
      </g>
    </svg>
  )
}
