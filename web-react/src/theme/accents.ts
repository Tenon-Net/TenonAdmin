// 默认品牌蓝；保留旧靛蓝选项，兼容已保存的主色偏好。
export const ACCENTS = ['#0052D4', '#7C5CFF', '#0EA5E9', '#EC4899', '#F97316', '#10B981', '#646CFF'] as const

export type Accent = (typeof ACCENTS)[number]
