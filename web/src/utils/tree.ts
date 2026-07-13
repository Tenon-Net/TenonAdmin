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

/**
 * 按谓词剪枝:命中的节点保留(连同它的整棵子树——搜到一个目录,自然想看它下面有什么),
 * 未命中但有后代命中的节点作为**祖先链**保留(否则命中的节点在树里无处安放,只剩一堆没有上下文的叶子)。
 *
 * 命中节点返回的是**原对象引用**(零拷贝);只有"仅因后代命中而保留"的祖先才是浅拷贝(children 换成剪过的分支)。
 * 因此在搜索态下,直接往行对象上写值(`r.enabled = v`)对这些祖先行不会回写到源树 ——
 * 调用方要么改从源树写,要么变更后重拉(机构页选了后者,与它其余所有变更的做法一致)。
 *
 * 两个消费点:菜单管理页、机构管理页。
 */
export function filterTree<T extends { children?: T[] }>(nodes: T[], match: (node: T) => boolean): T[] {
  const out: T[] = []
  for (const node of nodes) {
    if (match(node)) {
      out.push(node)
      continue
    }
    const kids = filterTree(node.children ?? [], match)
    if (kids.length) out.push({ ...node, children: kids })
  }
  return out
}

/** 收集树中所有"可展开"节点(有子节点)的 id —— 受控展开时用它播种"全部展开"。 */
export function expandableIds<T extends { id: number; children?: T[] }>(nodes: T[]): number[] {
  const ids: number[] = []
  const walk = (list: T[]) => {
    for (const n of list) {
      if (n.children?.length) {
        ids.push(n.id)
        walk(n.children)
      }
    }
  }
  walk(nodes)
  return ids
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
