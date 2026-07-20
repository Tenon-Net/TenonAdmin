import { theme, type ThemeConfig } from 'antd'
import { derivePrimary } from '@/theme/mix'

// `Density` 归 app store(与 Vue 侧一致:类型跟着它的持久化字段走,不跟着消费方走)。
// 这里转口一下,免得主题桥的使用者为了一个类型去 import store。
import type { Density } from '@/stores/app'

export type { Density }

/** 读当前主题下的 token 值。`getComputedStyle` 同步反映最新的 `data-theme`,所以**必须先翻属性再读**。 */
const v = (name: string): string =>
  getComputedStyle(document.documentElement).getPropertyValue(name).trim()

/**
 * CSS 长度 → antd 数值 token。
 *
 * **这是 antd 与 Naive 的一处硬差别**:Naive 的 `borderRadius`/`fontSize` 收 `"10px"` 这样的字符串,
 * antd 收的是**数字**。把 `"10px"` 原样塞进去 TS 会红;塞 `NaN` 不会红,只会让圆角/字号诡异失效。
 * 解析不出来时返回 `undefined`,**但那个键必须由 `defined()` 丢掉、不能下发**(原因见下)。
 */
export function num(raw: string): number | undefined {
  const n = Number.parseFloat(raw)
  return Number.isFinite(n) ? n : undefined
}

/**
 * 丢掉"没值"的键(`undefined` 或空串),让 antd 的种子值真正留住。
 *
 * **不能指望"传 `undefined` = 用默认值"** —— antd 内部是朴素展开 `{...seedToken, ...config.token}`,
 * 而展开**会复制值为 `undefined` 的自有键**,于是种子被覆盖成 `undefined`。实测(6.5.1):
 *   `borderRadius: undefined` → `borderRadius`/`LG`/`SM` **全变 undefined**,`fontSizeLG` 直接 **NaN**
 *   `colorPrimary: ''`        → 派生出一整条 **#000000** 的黑色阶,而不是退回 antd 的蓝
 * 也就是说"传 undefined 更安全"这个直觉是反的:它把 NaN 从一个 token 挪到了一批派生 token 上。
 * 真正安全的是**根本不出现这个键**。
 *
 * 这条同时兜住 `tokens.css` 没进文档的情形(那时所有 `v()` 都返回空串)——
 * 页面会退回 antd 原生外观,而不是变成一片黑加一堆 NaN 尺寸。
 *
 * 比较必须是**严格**的:宽松 `!=` 下 `0 != ''` 为假,合法的 `0`(方角主题的 `--radius-sm: 0`)会被误杀。
 * `NaN` 也一并丢掉 —— 今天 `num()` 已把它折成 `undefined`,但只要有人在映射表里直接写
 * `Number.parseFloat(...)`,NaN 就会原样下发,正是本注释声称要根除的东西。
 * 返回 `Partial<T>`:实际返回的键比入参少,声明成 `T` 是在撒谎(今天无害,因为 antd 的 token 全可选)。
 */
export function defined<T extends object>(o: T): Partial<T> {
  return Object.fromEntries(
    Object.entries(o).filter(([, x]) => x !== undefined && x !== '' && !Number.isNaN(x)),
  ) as Partial<T>
}

export interface ThemeOpts {
  dark: boolean
  accent: string
  density: Density
}

/**
 * `tokens.css` → antd `ConfigProvider` 的 `theme`。
 *
 * 与 Vue 版 `naive-theme.ts` 同源同规则,但**明显更短**:antd 自己会从 `colorPrimary`/`colorError` 等
 * 种子色派生 hover/active 的整条色阶,不像 Naive 那样每个语义色都要手工给四态
 * (那边的 `semantic()` 派生在这里整个不需要)。
 *
 * 调用前提:`data-theme` 已经打在 `<html>` 上 —— 本函数读的是**当前**主题下的变量值。
 */
export function buildAntdTheme(opts: ThemeOpts): ThemeConfig {
  const p = derivePrimary(opts.accent, opts.dark, v('--color-bg-container') || undefined)

  // 暗色/亮色二选一,紧凑档再叠一层。antd 的 algorithm 支持数组,按顺序合成。
  const algorithm = [opts.dark ? theme.darkAlgorithm : theme.defaultAlgorithm]
  if (opts.density === 'compact') algorithm.push(theme.compactAlgorithm)

  return {
    algorithm,
    token: defined({
      colorPrimary: p.primary,
      colorSuccess: v('--color-success'),
      colorWarning: v('--color-warning'),
      colorError: v('--color-danger'),
      colorInfo: v('--color-info'),

      colorBgLayout: v('--color-bg-body'),
      colorBgContainer: v('--color-bg-container'),
      colorBgElevated: v('--color-bg-elevated'),

      colorText: v('--color-text-primary'),
      colorTextHeading: v('--color-text-primary'),
      colorTextSecondary: v('--color-text-secondary'),
      colorTextTertiary: v('--color-text-tertiary'),
      colorTextDescription: v('--color-text-tertiary'),
      colorTextPlaceholder: v('--color-text-tertiary'),
      colorTextDisabled: v('--color-text-disabled'),
      // `colorTextPlaceholder` 与 `colorTextDisabled` 在 antd 里都 = `colorTextQuaternary`(三者恒等)。
      // 我们的设计系统把「占位」与「禁用」分成两个色阶,所以这条恒等是**故意打破**的 ——
      // 已登记在 antd-theme.spec.ts 的豁免表里。这里把 quaternary 对齐到占位色,至少让它有个归属,
      // 不然它会独自留在 antd 的 rgba(0,0,0,0.25) 上,变成页面上第三种灰。
      colorTextQuaternary: v('--color-text-tertiary'),

      colorBorder: v('--color-border'),
      // antd 自己的 map 层里 `colorBorderDisabled` 与 `colorBorder` 是同一个表达式。只覆盖前者的话,
      // 禁用态输入框的边框会留在 antd 的中性阶(暗色 #424242 对我们的 #363A42)。
      colorBorderDisabled: v('--color-border'),
      colorSplit: v('--color-border'),
      // Card / Table / Tabs / Slider 画边框用的是 **colorBorderSecondary**,不是 colorBorder。
      // 不给的话它走 antd 自派生的中性阶(暗色 #303030),与手写 `border: 1px solid var(--color-border)`
      // 的 #363A42 对不上 —— 和刚修掉的「一页两种紫」同一类缺陷,只是换成了边框、差值更小更难发现。
      colorBorderSecondary: v('--color-border'),

      // 填充阶:每个都要连同 antd 的 alias 伙伴一起给,否则又是「同一页两种灰」。
      // `colorFillAlter = colorFillQuaternary`、`controlItemBgHover = colorFillTertiary`(alias.js),
      // 只覆盖前者时后者仍是 antd 的半透明 rgba(0,0,0,0.02) —— 我们的填充是不透明色,差得很明显。
      // 阶梯顶端。漏掉它的后果不是深浅差一档,是**种类**差异:它驱动 colorBgTextActive /
      // colorFillContentHover,不给的话文字按钮 hover 是不透明 #EBEDF0、pressed 却是半透明黑,
      // 落在有色或图案底上当场露馅。而恒等闸门**结构性看不见它** —— 那两对的两侧都不在我们的
      // 覆盖集里,`touched` 直接把它们滤掉了。
      // **按下**态,必须与 hover 档不同色。filled 按钮与 Input.Search 把三态接到
      // colorFillTertiary(静息) / colorFillSecondary(hover) / colorFill(active),
      // 把后两个给同一个变量的话按下去毫无视觉反馈 —— 而恒等闸门看不见(这对不是 antd 的恒等),
      // 半透明检查也看不见(两个值都不透明)。下面有一条专门断三态互不相同的用例。
      colorFill: v('--color-fill-active'),
      colorFillAlter: v('--color-fill'),
      colorFillQuaternary: v('--color-fill'),
      // `colorFillTertiary` 是**静息**填充,不是 hover:它驱动 Tag / filled Button / Slider 轨道 / Empty,
      // 以及(经 alias `colorBgContainerDisabled = colorFillTertiary`)**禁用输入框的底色**。
      // 这里曾经错给成 hover 色,于是整站的静息填充都比设计稿深一档,而禁用输入框跟着一起深。
      colorFillTertiary: v('--color-fill'),
      // `colorBgTextHover = colorFillSecondary`(alias),给它 hover 色,文字按钮 hover 才与行 hover 同色。
      colorFillSecondary: v('--color-fill-hover'),
      // 恒等 `controlItemBgHover = colorFillTertiary` 在这里被**有意打破**:下拉/菜单项的 hover 该用
      // hover 色,而 colorFillTertiary 已经归静息了。已登记进 spec 的 EXEMPT 并附理由 ——
      // **豁免必须显式**,否则它和"忘了"长得一模一样。
      controlItemBgHover: v('--color-fill-hover'),
      // **不要**在这里覆盖 `controlItemBgActive`。antd 的 alias 层默认就是 `controlItemBgActive = colorPrimaryBg`
      // (node_modules/antd/lib/theme/util/alias.js),二者本该恒等。曾经这里写的是我们线性 mix 出来的 `p.light`,
      // 于是 Select/Menu/Table 的选中底色与 `var(--color-primary-light)`(由 antd 的 colorPrimaryBg 写回)分叉:
      // 亮色 #f0f0ff vs #f0f3ff、暗色 #303450 vs #34365b —— 又一次「同一页两种浅主色」,且这次明暗都有。
      // 分工是:共享层给种子,色阶归 UI 库自己派生。别再往回抢。

      borderRadius: num(v('--radius-md')),
      borderRadiusLG: num(v('--radius-lg')),
      borderRadiusSM: num(v('--radius-sm')),
      fontSize: num(v('--font-size-base')),
      fontFamily: v('--font-family-base'),
      fontFamilyCode: v('--font-family-mono'),

      // v6 是 boxShadow/Secondary/Tertiary,**不是** Naive 那套 boxShadow1/2/3。
      // antd 默认 boxShadow 与 boxShadowSecondary 是**同一个**模板,我们给三档不同的值属于**故意**分开
      //(设计系统就是三级)。**这里原先写着"已登记豁免"——那是假的**:EXEMPT 里从来只有
      // colorTextDisabled|colorTextQuaternary 一条,而且这两个键根本不是 `x: mergedToken.y` 形式的
      // 转发赋值,恒等闸门压根不评估它们,没有需要豁免的东西。假注释比没注释坏:它让人以为有守卫。
      // **注意这三个不是序数阶梯,是角色名 —— 别按 1/2/3 对号入座。**Naive 的 boxShadow1/2/3 是
      // 由小到大,而 antd 这三个的实际用法是(逐个 grep `token.xxx` 的消费者核过):
      //   boxShadow          → 仅 Modal          → 给最重的 --shadow-3(浮层/抽屉档)
      //   boxShadowSecondary → Dropdown/Select/Tabs/FloatButton → --shadow-2(弹层档)
      //   boxShadowTertiary  → message / Segmented 选中滑块     → 最轻的 --shadow-1(卡片档)
      // antd 原档也印证:boxShadow 与 boxShadowSecondary **值完全相同**(都是 6/16 大浮层),
      // 而 Tertiary 是 1px/2px 的微阴影。按序号映射的话,Modal 会挂上卡片级微阴影,
      // 而一个 24px 高的 Segmented 滑块下面挂 48px 模糊、0.18 alpha —— 比原档重约一个数量级。
      boxShadow: v('--shadow-3'),
      boxShadowSecondary: v('--shadow-2'),
      boxShadowTertiary: v('--shadow-1'),
      // 阴影**颜色**。上面三条只覆盖了三个具名阴影,而抽屉 / 气泡箭头 / 标签溢出等十来个阴影是 antd
      // 拿 `colorShadow` 自派生的。不给的话暗色下它回落 `rgba(255,255,255,0.2)` —— 抽屉是**白色发光**。
      // 基色,不是阴影值 —— antd 会把它的 alpha 乘进每一层派生阴影,所以令牌那边必须是不透明色。
      colorShadow: v('--color-shadow'),
    }),
    components: {
      // 卡片圆角走 lg(12);常规控件走 token.borderRadius(md=10)。
      Card: defined({ borderRadiusLG: num(v('--radius-lg')) }),
      // compactAlgorithm 不管表格行高,单元格纵向内边距要另给。
      Table: opts.density === 'compact' ? { cellPaddingBlock: 8, cellPaddingBlockSM: 6 } : {},
    },
  }
}
