// 6 主色候选(design_handoff §可配置项)。默认 = 第一个 corporate 蓝 #0082CE(spike/theme-refresh:采 daisyUI corporate 主色)。
export const ACCENTS = ['#0082CE', '#7C5CFF', '#0EA5E9', '#EC4899', '#F97316', '#10B981'] as const

export type Accent = (typeof ACCENTS)[number]
