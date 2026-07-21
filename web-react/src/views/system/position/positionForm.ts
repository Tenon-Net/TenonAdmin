// 岗位新增/编辑表单的**纯逻辑**。抽出便于 positionForm.spec 变异钉死 —— 页面 index.tsx 只做接线。
// 岗位字段少、无透传字段(不像 user 有 orgId 等要防清空),表单模型即 `PositionInput` 本身,不另立类型。
import type { PositionInput, SysPosition } from '@/types/api'

/** 新增空表单默认值:排序 0、默认启用(建出即可用)。 */
export const blankForm = (): PositionInput => ({ name: '', code: '', sort: 0, enabled: true })

/**
 * 行 → 全量入参。**行内启停(StatusSwitch)与编辑回填共用**:后端无独立启停端点,两者都走全量 `update`,
 * 而全量替换语义下**少回传一个字段就会把该行的其它字段抹空**。这是本页唯一会静默出错的接缝,单测钉死。
 * 只取 4 个业务字段(丢掉 id/createTime —— PositionInput 也不含它们)。
 */
export const rowToInput = (r: SysPosition): PositionInput => ({
  name: r.name,
  code: r.code,
  sort: r.sort,
  enabled: r.enabled,
})
