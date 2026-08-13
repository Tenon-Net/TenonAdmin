// 角色页 CRUD 表单纯逻辑:全量入参映射 + 默认表单。抽出做变异钉,index.tsx 只接线。
import type { RoleInput, SysRole } from '@/types/api'

/** 行 → 全量入参:openEdit 回填与 StatusSwitch 行内改状态共用(无独立启停端点,走全量 update,漏字段抹空;remark 归一空串)。变异钉。 */
export function roleToInput(r: SysRole): RoleInput {
  return { name: r.name, code: r.code, sort: r.sort, enabled: r.enabled, remark: r.remark ?? '', isDelegatable: r.isDelegatable ?? false }
}

/** 新增默认表单。 */
export function blankRole(): RoleInput {
  return { name: '', code: '', sort: 0, enabled: true, remark: '', isDelegatable: false }
}
