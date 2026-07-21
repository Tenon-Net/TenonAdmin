import { useMemo } from 'react'
import { Select, type SelectProps } from 'antd'
import { useDictOptions } from '@/stores/dict'
import type { DictItem } from '@/types/api'

/** 停用项过滤 + 映射成 antd option 形状。DictSelect 的纯逻辑,变异钉在这。 */
export function toDictOptions(items: readonly DictItem[]): { label: string; value: string }[] {
  return items.filter((i) => i.enabled).map((i) => ({ label: i.label, value: i.value }))
}

/**
 * 字典下拉:typeCode 取数(经 stores/dict 缓存 + 并发去重),停用项过滤,其余 props 全透传 antd Select。
 * 对齐 Vue 侧 DictSelect 的 `$attrs` 透传。唯一差别:Vue 那版给了 loading 圈,这里 `useDictOptions` 不暴露
 * 在途态,字典通常极小且缓存命中、首帧后一拍即出 —— ponytail: 省掉 loading 圈,真遇到慢字典再从 store 引出在途态。
 */
export function DictSelect({ typeCode, ...rest }: { typeCode: string } & Omit<SelectProps, 'options'>) {
  const items = useDictOptions(typeCode)
  const options = useMemo(() => toDictOptions(items), [items])
  return <Select options={options} {...rest} />
}
