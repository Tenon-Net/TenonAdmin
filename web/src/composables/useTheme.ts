import { ref, watch, type Ref } from 'vue'
import { darkTheme, type GlobalTheme, type GlobalThemeOverrides } from 'naive-ui'
import { useAppStore } from '@/stores/app'
import { buildThemeOverrides } from '@/theme/naive-theme'
import { derivePrimary } from '@/theme/mix'

/**
 * 主题落地:随 app.isDark / app.accent / app.density / app.grayscale 变化,
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
    // 容器色要在 data-theme 打上之后读(apply() 里的顺序),否则暗色首帧会拿到亮色容器。
    const container = getComputedStyle(el).getPropertyValue('--color-bg-container').trim() || undefined
    const p = derivePrimary(app.accent, app.isDark, container)
    el.style.setProperty('--color-primary', p.primary)
    el.style.setProperty('--color-primary-hover', p.hover)
    el.style.setProperty('--color-primary-pressed', p.pressed)
    el.style.setProperty('--color-primary-light', p.light)
  }

  function apply() {
    const el = document.documentElement
    el.setAttribute('data-theme', app.isDark ? 'dark' : '')
    el.setAttribute('data-density', app.density)
    el.toggleAttribute('data-gray', app.grayscale) // 灰阶滤镜(styles/index.css)
    applyPrimaryVars()
    naiveTheme.value = app.isDark ? darkTheme : null
    overrides.value = buildThemeOverrides({ dark: app.isDark, accent: app.accent })
  }

  watch([() => app.isDark, () => app.accent, () => app.density, () => app.grayscale], apply, { immediate: true })

  return { overrides, naiveTheme }
}
