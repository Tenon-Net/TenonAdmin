import { Tag } from 'antd'
import { useDictOptions } from '@/stores/dict'
import type { DictItem } from '@/types/api'

/**
 * antd Tag 的 color 用预设语义色字符串(processing=蓝 / success=绿 / warning=橙 / error=红 / default=灰)。
 * Vue(naive)那版 FALLBACK 是 info/success/warning/error;naive 的 info(蓝)在 antd 对应 processing。
 * 后端加 tagType 字段后改读字段、废弃本 fallback 约定。
 */
export type TagColor = 'default' | 'processing' | 'success' | 'warning' | 'error'
const FALLBACK: TagColor[] = ['processing', 'success', 'warning', 'error']

/**
 * 字典值 → 标签。DictTag 的纯逻辑,变异钉在这:
 *   - 空值(null/undefined/'')→ null(上层渲染「—」,不出空标签)
 *   - 命中字典 → label 取字典文案,color 按启用项**排序下标** i%4 取 FALLBACK(后端 sort 间接定色)
 *   - 未命中(未加载完/脏值)→ label 取原始 value(不空白不闪烁),color 取 default
 *   - typeMap 显式映射优先于 FALLBACK 约定
 */
export function resolveDictTag(
  items: readonly DictItem[],
  value: string | null | undefined,
  typeMap?: Record<string, TagColor>,
): { label: string; color: TagColor } | null {
  if (value == null || value === '') return null
  const enabled = items.filter((i) => i.enabled)
  const idx = enabled.findIndex((i) => i.value === value)
  const label = idx >= 0 ? enabled[idx].label : value
  const color = typeMap?.[value] ?? (idx >= 0 ? FALLBACK[idx % FALLBACK.length] : 'default')
  return { label, color }
}

/** 表格列字典翻译 + 语义色标签。空值渲染「—」,未命中容错渲染原始值。 */
export function DictTag({ typeCode, value, typeMap }: {
  typeCode: string
  value?: string | null
  typeMap?: Record<string, TagColor>
}) {
  const items = useDictOptions(typeCode)
  const hit = resolveDictTag(items, value, typeMap)
  if (!hit) return <span>—</span>
  return (
    <Tag color={hit.color} bordered={false}>
      {hit.label}
    </Tag>
  )
}
