// 颜色派生规则(DESIGN.md §7.1 权威实现)。纯函数,无框架依赖。

function clampByte(n: number): number {
  return Math.max(0, Math.min(255, Math.round(n)))
}

function parseHex(hex: string): [number, number, number] {
  let h = hex.replace('#', '').trim()
  if (h.length === 3) h = h.split('').map((c) => c + c).join('')
  const n = Number.parseInt(h, 16)
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255]
}

function toHex(r: number, g: number, b: number): string {
  return '#' + [r, g, b].map((v) => clampByte(v).toString(16).padStart(2, '0')).join('')
}

/** 在 a、b 之间按 t∈[0,1] 线性插值(sRGB 分量)。t=0→a,t=1→b。 */
export function mix(a: string, b: string, t: number): string {
  const [ar, ag, ab] = parseHex(a)
  const [br, bg, bb] = parseHex(b)
  return toHex(ar + (br - ar) * t, ag + (bg - ag) * t, ab + (bb - ab) * t)
}

/** 主色四态。 */
export interface PrimaryRamp {
  primary: string
  hover: string
  pressed: string
  /** 浅底(选中背景 / 标签底)。 */
  light: string
}

/**
 * accent → 主色四态(DESIGN.md §7.1)。暗色先把 accent 提亮一档,再派生 hover/pressed/light。
 *
 * 放在共用层而不是各模板里:这是**纯色彩数学**,两个模板必须算出同一组值,否则同一个 accent
 * 在 Vue 版和 React 版会长得不一样。从前 Vue 侧就有两份实现(`naive-theme.ts` 与 `useTheme.ts`
 * 的 `applyPrimaryVars`),同样的三个魔数写了两遍 —— 再让 React 写第三遍,漂移只是时间问题。
 *
 * `darkContainer` 只在暗色下参与 `light` 的计算(浅底要往容器色靠,否则在暗底上白得刺眼);
 * 亮色下忽略。调用方通常传当前的 `--color-bg-container`。
 */
export function derivePrimary(accent: string, dark: boolean, darkContainer = '#1F2229'): PrimaryRamp {
  // 亮色下 primary 就是 accent 本身(不派生),但**要统一转小写**:其余三个都经 `toHex` 出小写,
  // 而 accent 通常是设计稿里的大写字面量。不归一的话,输出的大小写会随主题变化,
  // 任何拿 `===` 直接比的地方就会得到一个**只在亮色下失败**的不一致 —— 这类只在单一模式下露馅的
  // 不对称最难查(B2 的「一页两种紫」就是同一个形状)。CSS 本身不区分大小写,归一无副作用。
  const primary = (dark ? mix(accent, '#FFFFFF', 0.18) : accent).toLowerCase()
  return {
    primary,
    hover: mix(primary, '#FFFFFF', 0.16),
    pressed: mix(primary, '#000000', 0.18),
    light: dark ? mix(primary, darkContainer, 0.82) : mix(primary, '#FFFFFF', 0.9),
  }
}

/** accent → rgba(...,alpha),用于发光 / 渐变。 */
export function rgba(hex: string, alpha: number): string {
  const [r, g, b] = parseHex(hex)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

/**
 * 头像填充渐变(仅欢迎横幅的占位头像)。
 *
 * 登录页主按钮走 antd 平面主色,不铺渐变和光晕:按钮的层级靠尺寸和字重就够,
 * 且 accent→紫→粉这组配色是通用模板脸。留在头像是因为那里装饰即内容
 * (52px 圆形占位头像),且跟随 accent 换色。
 */
export function btnGrad(accent: string): string {
  return `linear-gradient(135deg, ${accent} 0%, ${mix(accent, '#8B5CF6', 0.55)} 55%, ${mix(accent, '#EC4899', 0.62)} 100%)`
}
