import type { GlobalThemeOverrides } from 'naive-ui'

// 品牌双栏与产品展示共用表单尺度和对比度，继承上层主色及组件配置。
export const loginFormTheme: GlobalThemeOverrides = {
  Input: {
    heightLarge: '46px',
    fontSizeLarge: '14px',
    color: 'var(--color-login-card)',
    colorFocus: 'var(--color-login-card)',
    textColor: 'var(--color-login-ink)',
    placeholderColor: 'var(--color-login-muted)',
    iconColor: 'var(--color-login-muted)',
    border: '1px solid var(--color-login-line)',
  },
  Form: { labelTextColor: 'var(--color-login-ink)' },
  Checkbox: { textColor: 'var(--color-login-muted)' },
}
