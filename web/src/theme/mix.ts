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

/** accent → rgba(...,alpha),用于发光 / 渐变。 */
export function rgba(hex: string, alpha: number): string {
  const [r, g, b] = parseHex(hex)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

/** 英雄主按钮渐变(仅登录页 / 欢迎横幅 / 头像)。 */
export function btnGrad(accent: string): string {
  return `linear-gradient(135deg, ${accent} 0%, ${mix(accent, '#8B5CF6', 0.55)} 55%, ${mix(accent, '#EC4899', 0.62)} 100%)`
}

/** 英雄按钮发光阴影。 */
export function glowSh(accent: string): string {
  return `0 6px 20px ${rgba(accent, 0.42)}`
}
