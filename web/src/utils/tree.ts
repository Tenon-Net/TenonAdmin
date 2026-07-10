/** 平铺列表 → 树。按 parentId 挂到父的 children;根 = parentId 不在集合内(含 0)。
 *  原序保留(输入通常已按 sort、id 排好);孤儿(父不存在)兜底当根,不丢数据。
 *  两个消费点:OrgTreeSelect(R4)、机构管理页(R9)。 */
export type Tree<T> = T & { children?: Tree<T>[] }

export function buildTree<T extends { id: number; parentId: number }>(flat: T[]): Tree<T>[] {
  const map = new Map<number, Tree<T>>()
  for (const item of flat) map.set(item.id, { ...item })
  const roots: Tree<T>[] = []
  for (const item of flat) {
    const node = map.get(item.id)!
    const parent = map.get(item.parentId)
    if (parent) (parent.children ??= []).push(node)
    else roots.push(node)
  }
  return roots
}

/** 收集 rootId 及其全部后代 id(平铺数组上按 parentId 迭代到不动点)。编辑机构选上级时剪自身子树防成环。 */
export function collectSubtreeIds<T extends { id: number; parentId: number }>(flat: T[], rootId: number): Set<number> {
  const ids = new Set<number>([rootId])
  let grew = true
  while (grew) {
    grew = false
    for (const item of flat) {
      if (!ids.has(item.id) && ids.has(item.parentId)) {
        ids.add(item.id)
        grew = true
      }
    }
  }
  return ids
}
