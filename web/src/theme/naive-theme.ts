import type { GlobalThemeOverrides } from 'naive-ui'
import { mix } from './mix'

// 读取当前主题下的 token 值(getComputedStyle 同步反映最新的 data-theme)。
const v = (name: string): string =>
  getComputedStyle(document.documentElement).getPropertyValue(name).trim()

/** 主色四态(DESIGN §7.1):暗色先把 accent 提亮一档,再派生 hover/pressed。 */
function derivePrimary(accent: string, dark: boolean) {
  const primary = dark ? mix(accent, '#FFFFFF', 0.18) : accent
  return {
    primary,
    hover: mix(primary, '#FFFFFF', 0.16),
    pressed: mix(primary, '#000000', 0.18),
  }
}

/** 语义色 hover/pressed/suppl 统一派生。 */
function semantic(base: string) {
  return {
    base,
    hover: mix(base, '#FFFFFF', 0.16),
    pressed: mix(base, '#000000', 0.18),
    suppl: mix(base, '#FFFFFF', 0.16),
  }
}

/**
 * tokens.css → Naive UI GlobalThemeOverrides(DESIGN.md §7 映射表)。
 * 主色由 accent 按派生规则算;其余颜色/度量从当前 CSS 变量读取。
 */
export function buildThemeOverrides(opts: { dark: boolean; accent: string }): GlobalThemeOverrides {
  const p = derivePrimary(opts.accent, opts.dark)
  const ok = semantic(v('--color-success'))
  const warn = semantic(v('--color-warning'))
  const err = semantic(v('--color-danger'))
  const info = semantic(v('--color-info'))
  return {
    common: {
      primaryColor: p.primary,
      primaryColorHover: p.hover,
      primaryColorPressed: p.pressed,
      primaryColorSuppl: p.hover,

      successColor: ok.base, successColorHover: ok.hover, successColorPressed: ok.pressed, successColorSuppl: ok.suppl,
      warningColor: warn.base, warningColorHover: warn.hover, warningColorPressed: warn.pressed, warningColorSuppl: warn.suppl,
      errorColor: err.base, errorColorHover: err.hover, errorColorPressed: err.pressed, errorColorSuppl: err.suppl,
      infoColor: info.base, infoColorHover: info.hover, infoColorPressed: info.pressed, infoColorSuppl: info.suppl,

      bodyColor: v('--color-bg-body'),
      cardColor: v('--color-bg-container'),
      tableColor: v('--color-bg-container'),
      modalColor: v('--color-bg-elevated'),
      popoverColor: v('--color-bg-elevated'),

      textColorBase: v('--color-text-primary'),
      textColor1: v('--color-text-primary'),
      textColor2: v('--color-text-secondary'),
      textColor3: v('--color-text-tertiary'),
      placeholderColor: v('--color-text-tertiary'),
      textColorDisabled: v('--color-text-disabled'),

      borderColor: v('--color-border'),
      dividerColor: v('--color-border'),
      actionColor: v('--color-fill'),
      tableHeaderColor: v('--color-fill'),
      hoverColor: v('--color-fill-hover'),

      borderRadius: v('--radius-md'),
      borderRadiusSmall: v('--radius-sm'),
      fontSize: v('--font-size-base'),
      fontSizeMedium: v('--font-size-base'),
      fontFamily: v('--font-family-base'),
      fontFamilyMono: v('--font-family-mono'),

      boxShadow1: v('--shadow-1'),
      boxShadow2: v('--shadow-2'),
      boxShadow3: v('--shadow-3'),
    },
    // 卡片圆角走 lg(12);常规控件走 common.borderRadius(md=10)。
    Card: { borderRadius: v('--radius-lg') },
    // 表头质感(corporate 风):次级色 + 半粗字重,与数据行拉开层次。
    DataTable: {
      thTextColor: v('--color-text-secondary'),
      thFontWeight: '600',
    },
  }
}
