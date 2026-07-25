import { describe, expect, it } from 'vitest'
import { derivePrimary, mix } from './mix'
import { buildThemeOverrides } from './naive-theme'

/**
 * 期望值一律取自**被测链路之外**(输入本身、分量大小方向、混合端点),不拿 `mix()` 再算一遍当期望
 * ——那是回声,恒真。所以这里不钉具体色值,只钉「派生规则」本身:哪一档该往白走、哪一档该往黑走、
 * 暗色该不该先提亮。规则被改坏时这些会红,而重新调档位(魔数微调)不会误红。
 */

/** 取 sRGB 分量,用于判方向(变亮 = 分量整体上升)。 */
const rgb = (hex: string): [number, number, number] => {
  const n = Number.parseInt(hex.replace('#', ''), 16)
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255]
}
const sum = (hex: string) => rgb(hex).reduce((a, b) => a + b, 0)

const ACCENT = '#6366F1' // 色板里的大写字面量,与 stores/app.ts DEFAULTS 同形

describe('derivePrimary', () => {
  it('亮色下 primary 就是 accent 本身,只做小写归一(不派生)', () => {
    // 期望值来自输入,不来自 mix:这条钉的是「亮色不提亮」+「大小写不随主题漂移」。
    expect(derivePrimary(ACCENT, false).primary).toBe(ACCENT.toLowerCase())
  })

  it('暗色下先把 accent 提亮一档', () => {
    const dark = derivePrimary(ACCENT, true).primary
    expect(dark).not.toBe(ACCENT.toLowerCase())
    expect(sum(dark)).toBeGreaterThan(sum(ACCENT)) // 方向:更亮
  })

  it('hover 往白走、pressed 往黑走(明暗两档都成立)', () => {
    for (const dark of [false, true]) {
      const p = derivePrimary(ACCENT, dark)
      expect(sum(p.hover)).toBeGreaterThan(sum(p.primary))
      expect(sum(p.pressed)).toBeLessThan(sum(p.primary))
    }
  })

  it('light 亮色下往白靠、暗色下往容器色靠(而非往白靠)', () => {
    expect(sum(derivePrimary(ACCENT, false).light)).toBeGreaterThan(sum(ACCENT))

    // 暗色浅底若还往白走,会在暗底上白得刺眼——钉住它靠向传入的容器色。
    const container = '#1F2229'
    const light = derivePrimary(ACCENT, true, container).light
    const primary = derivePrimary(ACCENT, true).primary
    expect(sum(light)).toBeLessThan(sum(primary))
    expect(sum(light)).toBeGreaterThan(sum(container))
  })

  it('容器色缺省时用内置暗底,不炸', () => {
    // useTheme 读不到 --color-bg-container 时传 undefined(happy-dom / 首帧都可能)。
    expect(derivePrimary(ACCENT, true, undefined).light).toBe(derivePrimary(ACCENT, true, '#1F2229').light)
  })
})

/**
 * 这一条钉的**不是色值**(那会是回声),而是**接线**:喂 Naive 的那份主色必须来自 `derivePrimary`,
 * 而不是某处又抄了一份魔数。台账里 A6 的原始症状正是「两份实现,改一处忘另一处,
 * 裸 CSS 与 Naive 组件主色不同步,而没有任何东西会报错」——有了这条,再抄一份就会报错。
 * 写 CSS 变量的另一半(`useTheme.applyPrimaryVars`)已在浏览器里对 6 accent × 明暗实测过。
 */
describe('buildThemeOverrides 与裸 CSS 共用同一份主色派生', () => {
  it.each([
    ['light', false],
    ['dark', true],
  ])('%s 模式下 Naive 主色三态取自 derivePrimary', (_name, dark) => {
    const accent = '#EC4899'
    const p = derivePrimary(accent, dark)
    const common = buildThemeOverrides({ dark, accent }).common!
    expect(common.primaryColor).toBe(p.primary)
    expect(common.primaryColorHover).toBe(p.hover)
    expect(common.primaryColorPressed).toBe(p.pressed)
  })
})

describe('mix', () => {
  it('t=0 取 a、t=1 取 b(端点不插值)', () => {
    // 端点期望值是两个入参本身,与插值实现无关。
    expect(mix('#FF0000', '#00FF00', 0)).toBe('#ff0000')
    expect(mix('#FF0000', '#00FF00', 1)).toBe('#00ff00')
  })

  it('三位简写与六位等价', () => {
    expect(mix('#F00', '#FFFFFF', 0.5)).toBe(mix('#FF0000', '#FFFFFF', 0.5))
  })
})
