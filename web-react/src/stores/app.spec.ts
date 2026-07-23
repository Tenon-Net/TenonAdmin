import { describe, it, expect, beforeEach } from 'vitest'
import { ACCENTS } from '@/theme/accents'
import { useAppStore, isDark, LAYOUT_MODES } from './app'

beforeEach(() => {
  useAppStore.setState({ locale: 'zh-CN', collapsed: false, systemDark: false })
  useAppStore.getState().resetSettings()
  localStorage.clear()
})

describe('isDark', () => {
  it('themeScheme=auto 时随系统偏好翻转', () => {
    expect(useAppStore.getState().themeScheme).toBe('auto')
    expect(isDark(useAppStore.getState())).toBe(false)
    useAppStore.setState({ systemDark: true })
    expect(isDark(useAppStore.getState())).toBe(true)
  })

  it('显式 dark/light 覆盖系统偏好', () => {
    useAppStore.setState({ systemDark: false })
    useAppStore.getState().setThemeScheme('dark')
    expect(isDark(useAppStore.getState())).toBe(true)
    useAppStore.setState({ systemDark: true })
    useAppStore.getState().setThemeScheme('light')
    expect(isDark(useAppStore.getState())).toBe(false)
  })

  it('toggleDark 在当前生效侧基础上翻(auto + 系统暗 → light)', () => {
    useAppStore.setState({ systemDark: true }) // auto 下当前生效侧是暗
    useAppStore.getState().toggleDark()
    expect(useAppStore.getState().themeScheme).toBe('light')
  })
})

describe('resetSettings', () => {
  it('外观项回默认,但 locale / collapsed 保留', () => {
    const s = useAppStore.getState()
    s.setLocale('en-US')
    s.toggleCollapsed()
    s.setThemeScheme('dark')
    s.setAccent('#123456')
    s.setDensity('compact')
    s.setLayoutMode('horizontal')

    useAppStore.getState().resetSettings()

    const after = useAppStore.getState()
    expect(after.themeScheme).toBe('auto')
    expect(after.accent).toBe(ACCENTS[0])
    expect(after.density).toBe('comfortable')
    expect(after.layoutMode).toBe('vertical')
    expect(after.locale).toBe('en-US') // 未被重置
    expect(after.collapsed).toBe(true) // 未被重置
  })
})

describe('setSetting', () => {
  it('按 key 写界面开关,值即时生效', () => {
    const s = useAppStore.getState()
    expect(s.showBreadcrumb).toBe(true) // 默认
    s.setSetting('showBreadcrumb', false)
    expect(useAppStore.getState().showBreadcrumb).toBe(false)
    s.setSetting('pageTransition', 'fade-slide')
    expect(useAppStore.getState().pageTransition).toBe('fade-slide')
    s.setSetting('watermarkText', '内部')
    expect(useAppStore.getState().watermarkText).toBe('内部')
  })
})

describe('persist merge 迁移', () => {
  it('localStorage 里的旧 layoutMode 不在合法集合内 → 回落 vertical;合法值不动', async () => {
    localStorage.setItem('app', JSON.stringify({ state: { layoutMode: 'mixed-nav' }, version: 0 }))
    await useAppStore.persist.rehydrate()
    expect(useAppStore.getState().layoutMode).toBe('vertical') // 已废弃的模式,merge 里回落默认值

    localStorage.setItem('app', JSON.stringify({ state: { layoutMode: 'horizontal' }, version: 0 }))
    await useAppStore.persist.rehydrate()
    expect(useAppStore.getState().layoutMode).toBe('horizontal') // 合法值不被动
  })

  it('LAYOUT_MODES 是迁移判据的唯一来源(集合里的每个值都能过水合)', async () => {
    // 走 rehydrate 而不是 setLayoutMode:后者(app.ts)**不做任何校验**,校验只在 merge 里,
    // 断言"裸 setter 会 set"与这条用例的名字毫无关系。
    for (const m of LAYOUT_MODES) {
      localStorage.setItem('app', JSON.stringify({ state: { layoutMode: m }, version: 0 }))
      await useAppStore.persist.rehydrate()
      expect(useAppStore.getState().layoutMode, `${m} 是合法值,不该被 merge 回落`).toBe(m)
    }
  })
})

// Vue 侧没有对应用例。`systemDark` 落盘不会有任何即时症状:只有"用户改了系统主题 + 下次打开"
// 这条链路才露馅,而那时看到的是"auto 档不跟随系统",极难联想到持久化。
describe('持久化白名单', () => {
  it('systemDark 不入库,外观项与 locale/collapsed 入库', () => {
    useAppStore.setState({ systemDark: true })
    useAppStore.getState().setAccent('#123456')
    useAppStore.getState().setLocale('en-US')

    const saved = JSON.parse(localStorage.getItem('app')!).state
    expect(saved).not.toHaveProperty('systemDark')
    expect(saved.accent).toBe('#123456')
    expect(saved.locale).toBe('en-US')
    expect(saved.collapsed).toBe(false)
  })

  it('落盘键集恰好是白名单本身,不多不少', () => {
    // 断言完整键集而不是"没有以 set/toggle 开头的键":后者漏掉 `resetSettings`(唯一不合那个命名模式的
    // action),而且在 partialize 是显式字面量的前提下由构造保证为真 —— 是条永远不会红的检查。
    useAppStore.getState().setAccent('#123456')
    const saved = JSON.parse(localStorage.getItem('app')!).state
    expect(Object.keys(saved).sort()).toEqual([
      'accent', 'collapsed', 'density', 'fixedHeader', 'formStyle', 'grayscale', 'layoutMode',
      'locale', 'pageTransition', 'showBreadcrumb', 'showTabs', 'themeScheme', 'watermark', 'watermarkText',
    ])

    // **漂移守卫**:Vue 的 `persist: true` 自动覆盖任何新增 state 字段,而这里的 partialize 是手写字面量。
    // 往 DEFAULTS 加一项而忘了加进 partialize,症状是"抽屉里改了、看着生效、F5 消失",四件套全绿。
    // 上面那条硬编码了键名清单,会和 partialize 一起变陈旧,所以抓不住这个漂移;这条从 DEFAULTS 反推。
    // 真源用 `exportSettings()` —— 它就是"DEFAULTS 同名键的现值",加字段时自动跟进,不用再维护一份清单。
    const missing = Object.keys(useAppStore.getState().exportSettings()).filter((k) => !(k in saved))
    expect(missing, `这些 DEFAULTS 字段没进 partialize:${missing.join(', ')}`).toEqual([])
  })
})
