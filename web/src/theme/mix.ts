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
 * 收在这里是因为从前有两份实现:`naive-theme.ts` 的私有 `derivePrimary`(算前三态,喂 Naive overrides)
 * 与 `useTheme.ts` 的 `applyPrimaryVars`(算四态,写 CSS 变量),同样的魔数写了两遍。
 * 改一处忘另一处 = 裸 CSS 与 Naive 组件主色不同步,**而没有任何东西会报错**。
 *
 * `darkContainer` 只在暗色下参与 `light`(浅底要往容器色靠,否则在暗底上白得刺眼);亮色下忽略。
 * 调用方通常传当前的 `--color-bg-container`。只要前三态的调用方(Naive)不关心 `light`,多算一次 mix 而已。
 */
export function derivePrimary(accent: string, dark: boolean, darkContainer = '#1F2229'): PrimaryRamp {
  // 亮色下 primary 就是 accent 本身(不派生),但**要统一转小写**:其余三个都经 `toHex` 出小写,
  // 而 accent 通常是色板里的大写字面量。不归一的话输出的大小写会随主题变化,任何拿 `===` 直接比的
  // 地方就会得到一个**只在亮色下失败**的不一致。CSS 不区分大小写,归一无副作用
  // (已核实:全仓唯一的 accent `===` 比较在 SettingsDrawer 色板选中态,两边都是原始 accent,不经此函数)。
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
 * 曾经也铺在登录页主按钮上、外加一圈 `glowSh` 光晕,两者已移除:按钮的层级靠尺寸和字重就够,
 * 渐变+光晕只是装饰,且 accent→紫→粉这组配色是通用模板脸。留在头像是因为那里装饰即内容
 * (52px 圆形占位头像),且跟随 accent 换色。
 */
export function btnGrad(accent: string): string {
  return `linear-gradient(135deg, ${accent} 0%, ${mix(accent, '#8B5CF6', 0.55)} 55%, ${mix(accent, '#EC4899', 0.62)} 100%)`
}
