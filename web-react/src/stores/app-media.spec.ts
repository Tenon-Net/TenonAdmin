import { describe, it, expect, vi, afterEach } from 'vitest'

/**
 * 系统深浅色那座桥(`app.ts` 模块顶层的 matchMedia)此前**完全没有守卫**:
 * 把查询串写成 `'(prefers-color-scheme: DARK-TYPO)'`,全部用例照样绿、tsc 与 lint 也绿 ——
 * 因为所有 `isDark` 用例都用 `setState({ systemDark })` 直接注入,**从不经过这座桥**。
 * 症状是「auto 档在所有设备上永远显示亮色」,而且没有任何即时线索。
 *
 * (事件名写错反而是安全的:`addEventListener('changez', …)` 会被 tsc 挡下。只有查询串是裸的。)
 *
 * 这里必须走 `resetModules` + 动态 import:桥是在**模块求值时**建的,桩要抢在它前面。
 */
type Listener = (e: { matches: boolean }) => void

function stubMatchMedia(initial: boolean) {
  const seen: { query?: string; listeners: Listener[] } = { listeners: [] }
  vi.stubGlobal('matchMedia', (query: string) => {
    seen.query = query
    return {
      matches: initial,
      addEventListener: (_t: string, fn: Listener) => seen.listeners.push(fn),
      removeEventListener: (_t: string, fn: Listener) => {
        const i = seen.listeners.indexOf(fn)
        if (i >= 0) seen.listeners.splice(i, 1)
      },
    }
  })
  return seen
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.resetModules()
})

describe('系统深浅色桥', () => {
  it('查的是标准的 prefers-color-scheme: dark', async () => {
    const seen = stubMatchMedia(false)
    vi.resetModules()
    await import('./app')
    expect(seen.query).toBe('(prefers-color-scheme: dark)')
  })

  it('初始 matches 进 systemDark,且 auto 档据它解析', async () => {
    stubMatchMedia(true)
    vi.resetModules()
    const { useAppStore, isDark } = await import('./app')
    expect(useAppStore.getState().systemDark).toBe(true)
    expect(isDark(useAppStore.getState())).toBe(true) // 默认 themeScheme 是 auto
  })

  it('系统切换会推进 store(注册的监听真的接上了)', async () => {
    const seen = stubMatchMedia(false)
    vi.resetModules()
    const { useAppStore, isDark } = await import('./app')
    expect(useAppStore.getState().systemDark).toBe(false)
    expect(seen.listeners).toHaveLength(1)

    seen.listeners[0]!({ matches: true }) // 模拟用户在系统里切到暗色
    expect(useAppStore.getState().systemDark).toBe(true)
    expect(isDark(useAppStore.getState())).toBe(true)
  })
})
