import { ref, watch, type Ref } from 'vue'
import { darkTheme, type GlobalTheme, type GlobalThemeOverrides } from 'naive-ui'
import { useAppStore } from '@/stores/app'
import { buildThemeOverrides } from '@/theme/naive-theme'
import { mix } from '@/theme/mix'

/**
 * 主题落地:随 app.dark / app.accent / app.density 变化,
 *  1) 打 data-theme / data-density 到 <html>(裸 CSS 与 tokens 跟着翻);
 *  2) 把 accent 派生的 --color-primary* 写到 <html>(令消费 tokens 的裸 CSS 换色);
 *  3) 重建 Naive themeOverrides(新对象触发 n-config-provider 重渲染)。
 */
export function useTheme() {
  const app = useAppStore()
  const overrides = ref<GlobalThemeOverrides>({}) as Ref<GlobalThemeOverrides>
  const naiveTheme = ref<GlobalTheme | null>(null)

  function applyPrimaryVars() {
    const el = document.documentElement
    const primary = app.dark ? mix(app.accent, '#FFFFFF', 0.18) : app.accent
    el.style.setProperty('--color-primary', primary)
    el.style.setProperty('--color-primary-hover', mix(primary, '#FFFFFF', 0.16))
    el.style.setProperty('--color-primary-pressed', mix(primary, '#000000', 0.18))
    const container = getComputedStyle(el).getPropertyValue('--color-bg-container').trim() || '#1F2229'
    el.style.setProperty('--color-primary-light', app.dark ? mix(primary, container, 0.82) : mix(primary, '#FFFFFF', 0.9))
  }

  function apply() {
    const el = document.documentElement
    el.setAttribute('data-theme', app.dark ? 'dark' : '')
    el.setAttribute('data-density', app.density)
    applyPrimaryVars()
    naiveTheme.value = app.dark ? darkTheme : null
    overrides.value = buildThemeOverrides({ dark: app.dark, accent: app.accent })
  }

  watch([() => app.dark, () => app.accent, () => app.density], apply, { immediate: true })

  return { overrides, naiveTheme }
}
