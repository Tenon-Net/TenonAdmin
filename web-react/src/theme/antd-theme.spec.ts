import { describe, it, expect, beforeAll, afterAll } from 'vitest'
import { theme } from 'antd'
import { defined, num, buildAntdTheme } from './antd-theme'
// `?raw` 取文本而不是让它进文档 —— 下面「无 tokens.css」那一组验的正是"样式没进文档"的失败模式,
// 顶层 import 会把它污染掉。需要样式的那一组自己在 beforeAll 里注 <style>。
// (`?raw` 拿到空串的话说明 vitest 的 `css` 开关是关的,见 vite.config.ts 里的说明;下面有自检。)
import tokensCss from '@/styles/tokens.css?raw'
// antd 的 alias 源码,用来把「X 恒等于 Y」的不变量**全量抽出来**而不是逐个手写。
// 用 `?raw` 而不是 `node:fs`:`@types/node` 一旦进 program,`process`/`__dirname` 就在**所有**
// 浏览器源码里也可用了(三重斜线引用挡不住,全局声明不是文件级的),tsconfig 里那条守卫当场失效。
import aliasSrc from 'antd/es/theme/util/alias.js?raw'

describe('num', () => {
  it('取得出数就返回数,取不出返回 undefined(而不是 NaN)', () => {
    expect(num('10px')).toBe(10)
    expect(num('0')).toBe(0)
    expect(num('')).toBeUndefined()
    expect(num('inherit')).toBeUndefined()
  })
})

describe('defined', () => {
  it('丢掉 undefined 与空串', () => {
    expect(defined({ a: 1, b: undefined, c: '' })).toEqual({ a: 1 })
  })

  it('丢掉 NaN', () => {
    expect(defined({ a: Number.NaN, b: 2 })).toEqual({ b: 2 })
  })

  it('保留 0 与 false —— 它们是合法值,不是"没值"', () => {
    // 方角主题的 `--radius-sm: 0` 会走到这里;宽松 `!=` 下 `0 != ''` 为假,那样会被误杀。
    expect(defined({ a: 0, b: false })).toEqual({ a: 0, b: false })
  })

  it('返回的是新对象,不改入参', () => {
    const src = { a: 1, b: undefined }
    defined(src)
    expect(src).toEqual({ a: 1, b: undefined })
  })
})

// 这一组**不加载 tokens.css**,验的正是"样式没进文档"那个失败模式。
describe('buildAntdTheme(无 tokens.css)', () => {
  it('CSS 变量全空时只下发主色,其余键根本不出现', () => {
    const cfg = buildAntdTheme({ dark: false, accent: '#646CFF', density: 'comfortable' })
    expect(Object.keys(cfg.token!)).toEqual(['colorPrimary'])

    const t = theme.getDesignToken(cfg)
    expect(Number.isFinite(t.borderRadius)).toBe(true)
    expect(Number.isFinite(t.fontSizeLG)).toBe(true) // 曾经的症状:borderRadius 传 undefined → 这里 NaN
    expect(t.colorBgContainer).not.toBe('#000000') // 曾经的症状:colorPrimary 传空串 → 派生一整条黑色阶
  })

  it('紧凑档会叠上 compactAlgorithm(表格行内边距随之收紧)', () => {
    const roomy = buildAntdTheme({ dark: false, accent: '#646CFF', density: 'comfortable' })
    const compact = buildAntdTheme({ dark: false, accent: '#646CFF', density: 'compact' })
    expect(roomy.algorithm).toHaveLength(1)
    expect(compact.algorithm).toHaveLength(2)
    expect(theme.getDesignToken(compact).controlHeight).toBeLessThan(theme.getDesignToken(roomy).controlHeight)
  })
})

/**
 * antd 的 alias 层声明了一批「X 恒等于 Y」的不变量(`controlItemBgActive = colorPrimaryBg`、
 * `colorFillAlter = colorFillQuaternary` …)。桥**只覆盖其中一侧**时,这个恒等就被单边掰断,
 * 于是同一页面上两个本该同色的地方出现两种颜色。
 *
 * 这个缺陷在本次移植里前后露头**五次**,每次都是靠人眼或 review 逐个抓到的。所以这里不再逐个钉,
 * 而是**从 antd 源码里把恒等对抽出来**全量比对 —— 以后再多出一处,不用谁想起来去看。
 *
 * 故意打破的恒等登记在 EXEMPT 里:**豁免必须是显式的、带理由的**,否则它和"忘了"长得一模一样。
 */
describe('不抢 antd 的色阶派生(alias 恒等对全量比对)', () => {
  let style: HTMLStyleElement

  beforeAll(() => {
    style = document.createElement('style')
    style.textContent = tokensCss
    document.head.appendChild(style)
  })
  afterAll(() => {
    style.remove()
    // 复位:属性打在共享的 <html> 上,留着会串到**同文件内后面的 describe**。
    // (跨 spec 文件不会串 —— vitest 默认 `isolate: true`,每个文件拿到全新环境。
    //  原先这里写的理由「同一个 happy-dom 环境」是错的:复位该做,但不是为了这个。)
    document.documentElement.removeAttribute('data-theme')
  })

  /** 形如 `colorFillAlter: mergedToken.colorFillQuaternary` 的纯转发赋值。 */
  const ALIAS_PAIRS: [string, string][] = [
    ...aliasSrc.matchAll(/^\s*([a-zA-Z]\w*):\s*mergedToken\.([a-zA-Z]\w*),?\s*$/gm),
  ].map((m) => [m[1]!, m[2]!])

  // map 层(themes/*/colors.js)的恒等抽不出来 —— 那里是 `getSolidColor(colorBgBase, 15)` 这类表达式,
  // 不是转发。已知的手工登记在这里,**而且必须区分明暗**:map 层在两种主题下是两份不同的表达式表。
  const MAP_PAIRS_BOTH: [string, string][] = [
    // 亮 15/15、暗 26/26,两种主题都恒等。
    ['colorBorder', 'colorBorderDisabled'],
  ]
  const MAP_PAIRS_DARK_ONLY: [string, string][] = [
    // 暗色下 `getSolidColor(bgBase, 26)` 是**三路**恒等:colorBorder / colorBorderDisabled / colorBgSpotlight。
    ['colorBorder', 'colorBgSpotlight'],
    // 暗色下 `getAlphaColor(textBase, 0.04)` → colorFillQuaternary / colorBgBlur。
    ['colorFillQuaternary', 'colorBgBlur'],
  ]
  const MAP_PAIRS_LIGHT_ONLY: [string, string][] = [
    // **只有亮色恒等**:亮色两者都是 `getSolidColor(colorBgBase, 0)`,暗色是 8 与 12(容器比弹层低一档)。
    // 我们的令牌恰好镜像了这个结构(亮色两者同为 #FFFFFF,暗色 #1F2229 / #262A31),
    // 所以这一对**不能**放进 BOTH —— 那会让暗色那轮红在一个非缺陷上。
    ['colorBgContainer', 'colorBgElevated'],
  ]

  /** 故意打破的恒等:键是 `a|b`,值是理由。 */
  const EXEMPT: Record<string, string> = {
    'colorTextDisabled|colorTextQuaternary':
      'antd 把「占位」与「禁用」并成一个 colorTextQuaternary;我们的设计系统分两个色阶,占位对齐 quaternary、禁用独立。',
    'colorBorder|colorBgSpotlight':
      'colorBgSpotlight 是 Tooltip 的**反色**浮层底(亮色下 rgba(0,0,0,0.85),深底浅字),本就不该跟随我们的表面阶。',
    'colorFillQuaternary|colorBgBlur':
      'colorBgBlur 是毛玻璃层的染色(亮色下 transparent),与填充阶不同源。',
    'controlItemBgHover|colorFillTertiary':
      'colorFillTertiary 归**静息**填充(Tag / filled Button / Slider / 禁用输入框底),而下拉与菜单项的 hover 该用 hover 色,故有意分开。',
  }

  it.each([
    ['亮色', false],
    ['暗色', true],
  ])('%s:凡是被桥覆盖了一侧的恒等对,两侧必须仍相等', (_name, dark) => {
    document.documentElement.setAttribute('data-theme', dark ? 'dark' : '')
    const cfg = buildAntdTheme({ dark, accent: '#646CFF', density: 'comfortable' })
    const ours = cfg.token as Record<string, unknown>
    const t = theme.getDesignToken(cfg) as unknown as Record<string, unknown>

    const pairs = [...ALIAS_PAIRS, ...MAP_PAIRS_BOTH, ...(dark ? MAP_PAIRS_DARK_ONLY : MAP_PAIRS_LIGHT_ONLY)]
    const touched = pairs.filter(([a, b]) => a in ours || b in ours)

    // **自检**:tokens.css 没注进来时 `ours` 只剩 colorPrimary,touched 会塌成个位数,
    // 下面的断言就成了摆设。先证明这一轮确实检查了东西。
    expect(Object.keys(ours).length, 'tokens.css 没被解析 —— 本条断言会恒真').toBeGreaterThan(15)
    expect(touched.length).toBeGreaterThan(5)
    // 钉死而不是给地板:`> 30` 只抓得住归零,抓不住「51 掉到 31」—— antd 一次 minor 重排,
    // 闸门可以少查 20 对而全绿。数值变了说明 alias 面变了,先看 diff 再改这个数(当前 antd 6.5.1)。
    expect(ALIAS_PAIRS.length, 'antd 的 alias 面变了 —— 先看 diff 再改这个数').toBe(51)

    const broken = touched
      .filter(([a, b]) => !(`${a}|${b}` in EXEMPT) && !(`${b}|${a}` in EXEMPT))
      .filter(([a, b]) => String(t[a]) !== String(t[b]))
      .map(([a, b]) => `${a}(${String(t[a])}) ≠ ${b}(${String(t[b])})`)

    expect(broken, `单边覆盖掰断了 antd 的恒等,同一页会出现两种颜色`).toEqual([])
  })

  /**
   * 豁免必须**双向**成立:登记了"有意打破"的那几对,今天必须真的还是断开的。
   *
   * 这条堵的是一个真实缺口:把 `colorFillTertiary` 从静息色改回 hover 色(一个真回归),
   * 恒等闸门**一条都不红** —— 因为改完之后 `controlItemBgHover` 与它反而又相等了,
   * 于是那条豁免静默变成多余。闸门只查"成对的值是否相等",不查"值是否符合设计意图"。
   * 而"你声称打破了但其实没打破"是可观测的结构事实,**不需要把任何令牌字面量抄进测试**。
   */
  it.each([
    ['亮色', false],
    ['暗色', true],
  ])('%s:每条 EXEMPT 登记的恒等,今天必须仍然是被打破的', (_name, dark) => {
    document.documentElement.setAttribute('data-theme', dark ? 'dark' : '')
    const t = theme.getDesignToken(
      buildAntdTheme({ dark, accent: '#646CFF', density: 'comfortable' }),
    ) as unknown as Record<string, unknown>
    const stale = Object.keys(EXEMPT)
      .map((k) => k.split('|') as [string, string])
      .filter(([a, b]) => String(t[a]) === String(t[b]))
      .map(([a, b]) => `${a} 与 ${b} 又相等了(${String(t[a])})`)
    expect(stale, '豁免已过期:要么删掉它,要么那处"有意打破"根本没生效').toEqual([])
  })

  /**
   * 填充阶要**整阶同源**。
   *
   * 我们的填充令牌全是不透明色,而 antd 的默认填充是半透明黑/白。漏掉阶梯里任何一个成员,
   * 后果不是深浅差一档而是**种类**差异 —— 例如漏掉 `colorFill` 时,文字按钮 hover 是不透明 #EBEDF0、
   * pressed(走 colorBgTextActive ← colorFill)却是半透明黑,落在有色或图案底上当场露馅。
   * 恒等闸门**结构性看不见这个**:那几对的两侧都不在我们的覆盖集里,`touched` 直接滤掉了。
   * 断"没有半透明残留"这个性质,不抄任何具体色值。
   */
  it.each([
    ['亮色', false],
    ['暗色', true],
  ])('%s:填充阶没有成员停在 antd 的半透明默认上', (_name, dark) => {
    document.documentElement.setAttribute('data-theme', dark ? 'dark' : '')
    const t = theme.getDesignToken(
      buildAntdTheme({ dark, accent: '#646CFF', density: 'comfortable' }),
    ) as unknown as Record<string, string>
    const LADDER = ['colorFill', 'colorFillSecondary', 'colorFillTertiary', 'colorFillQuaternary']
    const translucent = LADDER.filter((k) => /rgba?\([^)]*,\s*0?\.\d+\s*\)/.test(t[k] ?? ''))
    expect(translucent, '这几个还停在 antd 的半透明默认上,与不透明的兄弟档不同种').toEqual([])
  })

  /**
   * 三个具名阴影的**角色**没有被按序号错配。
   *
   * antd 这三个不是序数阶梯:`boxShadow` 只有 Modal 用、`boxShadowSecondary` 是 Dropdown/Select/Tabs、
   * `boxShadowTertiary` 是 message 与 Segmented 选中滑块(最小的那个)。而 Naive 的 boxShadow1/2/3
   * 是由小到大 —— 照序号搬过来,Modal 会挂卡片微阴影,Segmented 滑块会挂 48px 抽屉阴影。
   * 断的是**模糊半径的单调性**这个结构性质,不抄任何具体值。
   */
  it('具名阴影按角色由重到轻,没有按序号错配', () => {
    document.documentElement.setAttribute('data-theme', '')
    const t = theme.getDesignToken(
      buildAntdTheme({ dark: false, accent: '#646CFF', density: 'comfortable' }),
    ) as unknown as Record<string, string>
    const maxBlur = (sh: string) => Math.max(...[...sh.matchAll(/(\d+)px/g)].map((m) => Number(m[1])))
    expect(maxBlur(t.boxShadow!), 'Modal 的阴影不该比弹层轻').toBeGreaterThan(maxBlur(t.boxShadowSecondary!))
    expect(maxBlur(t.boxShadowSecondary!), '弹层阴影不该比 Segmented 滑块轻').toBeGreaterThan(maxBlur(t.boxShadowTertiary!))
  })

  /**
   * 阴影**量级**。
   *
   * 这条以前断的是 `not.toMatch(/255,255,255/)`(阴影不是白的)—— 那句话在坏与好两种状态下**都绿**:
   * `--color-shadow` 从 `rgba(20,27,45,0.16)` 到不透明 `#141B2D`,两种写法都不含 255,255,255。
   * 真正的缺陷是量级:antd 把基色的 alpha **乘进**每一层派生阴影
   * (`alias.js`: `getShadowColor = a => base.clone().setA(base.a * a)`),
   * 所以带 0.16 alpha 的基色会让抽屉阴影的三档从 0.08/0.12/0.05 塌成 0.0128/0.0192/0.008。
   * 基色不透明时,派生 alpha 就等于 antd 自己那套档位 —— 断这个。
   */
  const alphasOf = (shadow: string): number[] =>
    [...shadow.matchAll(/rgba?\([^)]*?,\s*([\d.]+)\s*\)/g)].map((m) => Number(m[1]))

  it.each([
    ['亮色', false],
    ['暗色', true],
  ])('%s:派生阴影的 alpha 是 antd 原档,没有被基色的 alpha 二次衰减', (_name, dark) => {
    document.documentElement.setAttribute('data-theme', dark ? 'dark' : '')
    const t = theme.getDesignToken(
      buildAntdTheme({ dark, accent: '#646CFF', density: 'comfortable' }),
    ) as unknown as Record<string, string>

    // 自检:tokens.css 没进文档时亮色会回落 antd 默认 `#000` —— 那也是不透明、派生 alpha 也正好是
    // 0.08/0.12/0.05,于是下面三条**全绿**,这一轮无法区分「我们的令牌对」和「令牌根本没到」。
    // 恒等用例有自检行,这里原先没有,标准不一致。
    expect(
      getComputedStyle(document.documentElement).getPropertyValue('--color-shadow').trim(),
      'tokens.css 没进文档 —— 本条会退回 antd 默认而恒真',
    ).not.toBe('')

    // 基色本身必须不透明。写成 rgba(...,1) 也算,所以断的是解析出来的 alpha 而不是字面量形状。
    expect(alphasOf(t.colorShadow!), `--color-shadow 带了 alpha,antd 会把它乘进每一层`).toEqual([])

    // 抽屉阴影是 antd 拿 colorShadow 自派生的十来个之一,不在我们覆盖的三个具名阴影里。
    expect(alphasOf(t.boxShadowDrawerLeft!)).toEqual([0.08, 0.12, 0.05])
    // 顺带钉住它确实不是白的(旧断言的那一半仍然有意义,只是不够)。
    expect(t.boxShadowDrawerLeft).not.toMatch(/255,\s*255,\s*255/)
  })
})
