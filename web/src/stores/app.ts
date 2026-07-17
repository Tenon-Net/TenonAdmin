import { defineStore } from 'pinia'
import { usePreferredDark } from '@vueuse/core'
import { ACCENTS } from '@/theme/accents'

// 模块级:建一次 matchMedia 监听;isDark getter 内响应式读取 → auto 下随系统实时翻。
const prefersDark = usePreferredDark()

export type Density = 'comfortable' | 'compact'
export type FormStyle = 'modal' | 'drawer'
export type Locale = 'zh-CN' | 'en-US'
export type ThemeScheme = 'light' | 'dark' | 'auto'
export type PageTransition = 'fade' | 'fade-slide' | 'none'
export type LayoutMode =
  | 'vertical'
  | 'vertical-mix'
  | 'vertical-hybrid-header-first'
  | 'horizontal'
  | 'top-hybrid-sidebar-first'
  | 'top-hybrid-header-first'

/** 布局模式全集(单一来源:抽屉卡片、布局 shell、迁移校验共用)。 */
export const LAYOUT_MODES: LayoutMode[] = [
  'vertical',
  'vertical-mix',
  'vertical-hybrid-header-first',
  'horizontal',
  'top-hybrid-sidebar-first',
  'top-hybrid-header-first',
]

/** 可被"恢复默认"重置的外观项(不含 locale / collapsed,那两项按会话保留)。 */
const DEFAULTS = {
  themeScheme: 'auto' as ThemeScheme,
  accent: ACCENTS[0] as string,
  density: 'comfortable' as Density,
  layoutMode: 'vertical' as LayoutMode,
  showBreadcrumb: true,
  showTabs: true,
  fixedHeader: true,
  pageTransition: 'fade' as PageTransition,
  grayscale: false,
  watermark: false,
  watermarkText: '',
  // FormContainer 的全局形态偏好(弹窗/抽屉);组件可 per-instance 覆盖。
  formStyle: 'modal' as FormStyle,
}

/** UI 偏好:主题模式 / 主色 / 密度 / 布局 / 界面开关 / 折叠 / 语言。持久化(localStorage key "app")。 */
export const useAppStore = defineStore('app', {
  state: () => ({
    ...DEFAULTS,
    collapsed: false,
    locale: 'zh-CN' as Locale,
  }),
  getters: {
    // auto 时按系统深浅色实时解析(prefersDark 是响应式 ref,getter 是 computed → 随之更新)。
    isDark(state): boolean {
      return state.themeScheme === 'auto' ? prefersDark.value : state.themeScheme === 'dark'
    },
  },
  actions: {
    setThemeScheme(s: ThemeScheme) {
      this.themeScheme = s
    },
    // 顶栏快捷切换:在当前生效的深浅色基础上翻到明确的一侧。
    toggleDark() {
      this.themeScheme = this.isDark ? 'light' : 'dark'
    },
    setAccent(hex: string) {
      this.accent = hex
    },
    setDensity(d: Density) {
      this.density = d
    },
    setLocale(l: Locale) {
      this.locale = l
    },
    toggleCollapsed() {
      this.collapsed = !this.collapsed
    },
    setLayoutMode(m: LayoutMode) {
      this.layoutMode = m
    },
    // 抽屉"恢复默认":还原外观项,保留 locale / collapsed。其余布尔项由抽屉 v-model 直接改。
    resetSettings() {
      Object.assign(this, DEFAULTS)
    },
    // 导出当前外观配置(DEFAULTS 同名键的现值);抽屉「复制配置」用,粘回 app.ts DEFAULTS 即为新默认。
    exportSettings(): Record<keyof typeof DEFAULTS, unknown> {
      const keys = Object.keys(DEFAULTS) as (keyof typeof DEFAULTS)[]
      return Object.fromEntries(keys.map((k) => [k, this[k]])) as Record<keyof typeof DEFAULTS, unknown>
    },
  },
  persist: {
    // 迁移:localStorage 残留的旧模式(mixed-nav / full-content / header-mixed)不在新集合 → 回落 vertical。
    afterHydrate: ({ store }) => {
      if (!LAYOUT_MODES.includes(store.layoutMode)) store.layoutMode = 'vertical'
    },
  },
})
