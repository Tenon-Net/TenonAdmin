import { useAppStore, isDark } from '@/stores/app'

/**
 * 品牌配色(明暗两版)。**固定品牌身份色 #646CFF,不随 accent 变**。
 * 值源自 design-mockups/brand/tenon-logo(-dark).svg,与 Vue 侧 TenonLogo.vue 逐字一致。
 */
export function logoColors(dark: boolean): { bg: string; mark: string } {
  return dark
    ? { bg: '#181A2A', mark: '#8B91FF' }
    : { bg: '#646CFF', mark: '#FFFFFF' }
}

/** Tenon 品牌徽标(滚筒刷 × T)。内联 SVG,明暗跟随 app store。对应 Vue 侧 `TenonLogo.vue`。 */
export function TenonLogo({ size = 28 }: { size?: number }) {
  const dark = useAppStore(isDark)
  const { bg, mark } = logoColors(dark)
  return (
    <svg width={size} height={size} viewBox="0 0 64 64" role="img" aria-label="Tenon">
      <rect width="64" height="64" rx="6" fill={bg} />
      <path d="M12 12H52V24H48V20H44V52H32V28H40V20H36V24H12Z" fill={mark} />
    </svg>
  )
}
