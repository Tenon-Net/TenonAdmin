import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createApp, type Ref } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import piniaPluginPersistedstate from 'pinia-plugin-persistedstate'

// vi.mock 工厂被提升到本文件所有 import 之前"注册",但真正执行是惰性的:发生在依赖图里
// 第一次 import '@vueuse/core' 时(即 './app' 的顶层 import),这必然早于本文件自己的顶层代码
// 跑到——所以工厂没法写回一个在本文件顶层声明的普通变量(TDZ)。用 vi.hoisted 建一个立即可用的
// 容器规避;容器内的真 ref 则在工厂里动态 import('vue') 现造(vue 此时已被 pinia 等间接加载过)。
const holder = vi.hoisted(() => ({ ref: undefined as unknown as Ref<boolean> }))
vi.mock('@vueuse/core', async (importOriginal) => {
  const { ref } = await import('vue')
  holder.ref = ref(false)
  const actual = await importOriginal<typeof import('@vueuse/core')>()
  return { ...actual, usePreferredDark: () => holder.ref }
})

import { useAppStore } from './app'
import { ACCENTS } from '@/theme/accents'

beforeEach(() => {
  setActivePinia(createPinia())
  holder.ref.value = false
})

describe('useAppStore.isDark', () => {
  it('themeScheme=auto 时随系统偏好 ref 翻转', () => {
    const app = useAppStore()
    expect(app.themeScheme).toBe('auto')
    expect(app.isDark).toBe(false)
    holder.ref.value = true
    expect(app.isDark).toBe(true)
  })

  it('显式 dark/light 覆盖系统偏好', () => {
    const app = useAppStore()
    holder.ref.value = false
    app.setThemeScheme('dark')
    expect(app.isDark).toBe(true)
    holder.ref.value = true
    app.setThemeScheme('light')
    expect(app.isDark).toBe(false)
  })

  it('toggleDark 在当前生效侧基础上翻(auto + 系统暗 → light)', () => {
    const app = useAppStore()
    holder.ref.value = true // auto 下当前生效侧是暗
    app.toggleDark()
    expect(app.themeScheme).toBe('light')
  })
})

describe('useAppStore.resetSettings', () => {
  it('外观项回默认,但 locale / collapsed 保留', () => {
    const app = useAppStore()
    app.setLocale('en-US')
    app.toggleCollapsed()
    app.setThemeScheme('dark')
    app.setAccent('#123456')
    app.setDensity('compact')
    app.setLayoutMode('horizontal')

    app.resetSettings()

    expect(app.themeScheme).toBe('auto')
    expect(app.accent).toBe(ACCENTS[0])
    expect(app.density).toBe('comfortable')
    expect(app.layoutMode).toBe('vertical')
    expect(app.locale).toBe('en-US') // 未被重置
    expect(app.collapsed).toBe(true) // 未被重置
  })
})

// happy-dom 在 Node 22+ 环境下 window.localStorage 现存已知为 undefined(Node 自带的实验性
// 全局 localStorage 抢占了探测),这里用一个内存 Storage 桩子顶上;unstubGlobals:true 保证测试
// 后自动复位,不污染其它文件。
function makeMemoryStorage(): Storage {
  const data = new Map<string, string>()
  return {
    getItem: (k: string) => data.get(k) ?? null,
    setItem: (k: string, v: string) => void data.set(k, v),
    removeItem: (k: string) => void data.delete(k),
    clear: () => data.clear(),
    key: (i: number) => Array.from(data.keys())[i] ?? null,
    get length() {
      return data.size
    },
  } as Storage
}

// pinia-plugin-persistedstate 的 hydrate 守卫依赖 `pinia.state.value` 已被正常"安装"过——
// 光 setActivePinia 不经 app.use(pinia) 时守卫会静默短路、hydrate 不生效。挂一个空壳 Vue app
// 把 pinia 正经装一遍,复刻 main.ts 的真实接线顺序,afterHydrate 才会真的跑到。
function mountAppStore(pinia: ReturnType<typeof createPinia>) {
  pinia.use(piniaPluginPersistedstate)
  createApp({}).use(pinia)
  setActivePinia(pinia)
  return useAppStore()
}

describe('useAppStore persist.afterHydrate 迁移', () => {
  it('localStorage 里的旧 layoutMode 值不在合法集合内 → 回落 vertical;合法值不动', () => {
    vi.stubGlobal('localStorage', makeMemoryStorage())

    localStorage.setItem('app', JSON.stringify({ layoutMode: 'mixed-nav' })) // 旧版本已废弃的模式
    const app1 = mountAppStore(createPinia())
    expect(app1.layoutMode).toBe('vertical') // 'mixed-nav' 已不在合法集合内,afterHydrate 回落默认值

    localStorage.setItem('app', JSON.stringify({ layoutMode: 'horizontal' })) // 合法值
    const app2 = mountAppStore(createPinia())
    expect(app2.layoutMode).toBe('horizontal') // 合法值不被 afterHydrate 动
  })
})
