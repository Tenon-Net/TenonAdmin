import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import { ACCENTS } from '@/theme/accents'

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

// 系统深浅色偏好。Vue 侧是 VueUse 的 `usePreferredDark()`(模块级建一次监听),这里换等价的裸 matchMedia。
// 放模块级而不是 hook 里:`isDark` 要能在组件外(路由守卫、主题桥)同步读到。
const mq = typeof window !== 'undefined' && window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null

/** 外观项的类型 = DEFAULTS 的形状,免得同一组字段写两遍(加一项只改 DEFAULTS)。 */
type AppSettings = typeof DEFAULTS

export interface AppState extends AppSettings {
  collapsed: boolean
  locale: Locale
  /**
   * 系统是否偏好暗色。**不持久化**——它是设备当下的状态,不是用户偏好;存下来会让换了系统主题
   * 的用户在首帧看到上次那一侧。由下面的 matchMedia 监听推入。
   */
  systemDark: boolean
  setThemeScheme: (s: ThemeScheme) => void
  toggleDark: () => void
  setAccent: (hex: string) => void
  setDensity: (d: Density) => void
  setLocale: (l: Locale) => void
  setLayoutMode: (m: LayoutMode) => void
  /** 设置抽屉里各界面开关(showBreadcrumb/showTabs/fixedHeader/pageTransition/grayscale/watermark/watermarkText/formStyle)的统一写入口。 */
  setSetting: <K extends keyof AppSettings>(key: K, value: AppSettings[K]) => void
  toggleCollapsed: () => void
  resetSettings: () => void
  /** 导出当前外观配置(DEFAULTS 同名键的现值);设置抽屉「复制配置」用,粘回 DEFAULTS 即为新默认。 */
  exportSettings: () => Record<keyof AppSettings, unknown>
}

/** auto 时按系统深浅色解析。写成纯选择器,组件内 `useAppStore(isDark)`、组件外 `isDark(useAppStore.getState())`。 */
export const isDark = (s: Pick<AppState, 'themeScheme' | 'systemDark'>): boolean =>
  s.themeScheme === 'auto' ? s.systemDark : s.themeScheme === 'dark'

/** UI 偏好:主题模式 / 主色 / 密度 / 布局 / 界面开关 / 折叠 / 语言。持久化(localStorage key "app")。 */
export const useAppStore = create<AppState>()(
  persist(
    (set, get) => ({
      ...DEFAULTS,
      collapsed: false,
      locale: 'zh-CN' as Locale,
      systemDark: mq?.matches ?? false,
      setThemeScheme: (s) => set({ themeScheme: s }),
      // 顶栏快捷切换:在当前生效的深浅色基础上翻到明确的一侧(auto + 系统暗 → light)。
      toggleDark: () => set((s) => ({ themeScheme: isDark(s) ? 'light' : 'dark' })),
      setAccent: (hex) => set({ accent: hex }),
      setDensity: (d) => set({ density: d }),
      setLocale: (l) => set({ locale: l }),
      setLayoutMode: (m) => set({ layoutMode: m }),
      // 计算键的对象 TS 只能推成 { [x: string]: 值联合 },与 Partial<AppState> 不重叠 —— 经 unknown 收窄(值已由签名约束)。
      setSetting: (key, value) => set({ [key]: value } as unknown as Partial<AppState>),
      toggleCollapsed: () => set((s) => ({ collapsed: !s.collapsed })),
      // 抽屉"恢复默认":还原外观项,保留 locale / collapsed(zustand 的 set 是浅合并,未列出的键原样保留)。
      resetSettings: () => set({ ...DEFAULTS }),
      // Vue 侧有而移植时漏掉的一个 action(`web/src/stores/app.ts:89`)。台账从 B 到 E 没有任何一条
      // 会碰到设置抽屉,所以它不会被后面的批次自动带出来 —— 现在补,不然就静默丢了。
      exportSettings: () => {
        const keys = Object.keys(DEFAULTS) as (keyof AppSettings)[]
        const s = get()
        return Object.fromEntries(keys.map((k) => [k, s[k]])) as Record<keyof AppSettings, unknown>
      },
    }),
    {
      name: 'app',
      // 白名单 = Vue 侧 state 的全部持久化项。`systemDark` 与所有 action 都不入库(见字段注释)。
      partialize: (s) => ({
        themeScheme: s.themeScheme,
        accent: s.accent,
        density: s.density,
        layoutMode: s.layoutMode,
        showBreadcrumb: s.showBreadcrumb,
        showTabs: s.showTabs,
        fixedHeader: s.fixedHeader,
        pageTransition: s.pageTransition,
        grayscale: s.grayscale,
        watermark: s.watermark,
        watermarkText: s.watermarkText,
        formStyle: s.formStyle,
        collapsed: s.collapsed,
        locale: s.locale,
      }),
      // 迁移(对应 Vue 侧 persist.afterHydrate):localStorage 残留的旧模式(mixed-nav / full-content /
      // header-mixed)不在新集合 → 回落 vertical,否则布局壳拿到一个 match 不上的值会渲染成空白。
      // 用 `merge` 而不是 `onRehydrateStorage`:前者在写入 state 前跑,后者拿到的是已生效的 state,
      // 想改还得回头 setState,而那时 `useAppStore` 尚在 TDZ 里(同步 storage 下水合发生在 create 内部)。
      merge: (persisted, current) => {
        const s = { ...current, ...(persisted as Partial<AppState>) }
        if (!LAYOUT_MODES.includes(s.layoutMode)) s.layoutMode = 'vertical'
        return s
      },
    },
  ),
)

// 系统主题实时跟随:auto 档下用户在系统里切深浅色,页面立刻跟着翻。
const onSystemThemeChange = (e: MediaQueryListEvent) => useAppStore.setState({ systemDark: e.matches })
mq?.addEventListener('change', onSystemThemeChange)
// dev 下 Vite 每次热更新都会重跑本模块、再挂一个监听,旧的指向旧 store 实例且无人摘除,
// 随编辑次数线性累积。生产构建里这段被摇掉。
if (import.meta.hot) import.meta.hot.dispose(() => mq?.removeEventListener('change', onSystemThemeChange))
