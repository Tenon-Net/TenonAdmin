import { useEffect, useMemo, useState } from 'react'
import { TreeSelect, type TreeSelectProps } from 'antd'
import { orgApi } from '@/api'
import { buildTree, collectSubtreeIds, type Tree } from '@/utils/tree'
import type { SysOrg } from '@/types/api'

/**
 * 机构平铺 → 树选项。`excludeSubtreeOf`:编辑机构选上级时剪掉自身子树(防把自己挂到自己后代下成环)——
 * 用户页传 null(不剪),机构页传 editingId。纯逻辑,变异钉;buildTree/collectSubtreeIds 本身在 tree.spec 已测。
 */
export function orgTreeData(flat: SysOrg[], excludeSubtreeOf?: number | null): Tree<SysOrg>[] {
  if (excludeSubtreeOf == null) return buildTree(flat)
  const excluded = collectSubtreeIds(flat, excludeSubtreeOf)
  return buildTree(flat.filter((o) => !excluded.has(o.id)))
}

export interface OrgTreeSelectProps extends Omit<TreeSelectProps, 'treeData' | 'fieldNames'> {
  excludeSubtreeOf?: number | null
}

/**
 * 机构树下拉:挂载拉 orgApi.list()(平铺)→ buildTree 拼树;value/placeholder… 一切经 rest 透传 antd TreeSelect。
 * antd TreeSelect 无 loading prop(机构一次拉完、通常很快),不显式转圈——ponytail,真遇慢字典再包 Spin。
 */
export function OrgTreeSelect({ excludeSubtreeOf, ...rest }: OrgTreeSelectProps) {
  const [flat, setFlat] = useState<SysOrg[]>([])
  useEffect(() => {
    orgApi.list().then(setFlat).catch(() => {}) // 静默留空:机构是配角,不打断主流程
  }, [])
  const treeData = useMemo(() => orgTreeData(flat, excludeSubtreeOf), [flat, excludeSubtreeOf])
  return (
    <TreeSelect
      allowClear
      {...rest}
      // fieldNames 把 SysOrg 的 id/name/children 映射成 antd 期望的 value/label/children;
      // treeData 的元素形状因此与 antd DataNode 不同,需一处 cast。
      treeData={treeData as unknown as TreeSelectProps['treeData']}
      fieldNames={{ label: 'name', value: 'id', children: 'children' }}
    />
  )
}
