// 6 主色候选(design_handoff §可配置项)。默认 = 第一个靛蓝 #646CFF。
export const ACCENTS = ['#646CFF', '#7C5CFF', '#0EA5E9', '#EC4899', '#F97316', '#10B981'] as const

export type Accent = (typeof ACCENTS)[number]
