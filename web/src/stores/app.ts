import { defineStore } from 'pinia'
import { ACCENTS } from '@/theme/accents'

export type Density = 'comfortable' | 'compact'
export type Locale = 'zh-CN' | 'en-US'

/** UI 偏好:暗色 / 主色 / 密度 / 折叠 / 语言。持久化。 */
export const useAppStore = defineStore('app', {
  state: () => ({
    dark: false,
    accent: ACCENTS[0] as string,
    density: 'comfortable' as Density,
    collapsed: false,
    locale: 'zh-CN' as Locale,
  }),
  actions: {
    toggleDark() {
      this.dark = !this.dark
    },
    toggleCollapsed() {
      this.collapsed = !this.collapsed
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
  },
  persist: true,
})
