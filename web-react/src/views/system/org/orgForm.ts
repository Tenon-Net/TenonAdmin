import type { OrgInput, SysOrg } from '@/types/api'

/** 新增机构默认表单(parentId 0=根、启用、sort 0;category 空)。纯逻辑,变异钉。 */
export function blankForm(parentId = 0): OrgInput {
  return { parentId, name: '', code: '', category: null, sort: 0, enabled: true }
}

/**
 * 行 → 全量入参:openEdit 回填与 StatusSwitch 行内改状态**共用**——后端无独立启停端点,均走全量 update,
 * 漏一个字段就把该字段抹空,故须逐字段带全(category 归一 null)。变异钉。
 */
export function rowToInput(r: SysOrg): OrgInput {
  return { parentId: r.parentId, name: r.name, code: r.code, category: r.category ?? null, sort: r.sort, enabled: r.enabled }
}
