// 字典页纯逻辑:类型/项的全量入参映射 + 默认表单。抽出做变异钉,index.tsx 只接线。
// 全量 update 契约:StatusSwitch 行内改状态与编辑回填共用 toInput,漏字段即抹空,故逐字段带全(remark 归一空串)。
import type { DictItemInput, DictTypeInput, SysDictItem, SysDictType } from '@/types/api'

/** 类型行 → 全量入参(remark 归一空串;code 后端更新时忽略,仍带上保持全量)。变异钉。 */
export function typeToInput(r: SysDictType): DictTypeInput {
  return { code: r.code, name: r.name, sort: r.sort, enabled: r.enabled, remark: r.remark ?? '' }
}

/** 项行 → 全量入参(dictTypeCode 恒带,表单隐藏)。变异钉。 */
export function itemToInput(r: SysDictItem): DictItemInput {
  return { dictTypeCode: r.dictTypeCode, label: r.label, value: r.value, sort: r.sort, enabled: r.enabled }
}

/** 新增类型默认表单。 */
export function blankType(): DictTypeInput {
  return { code: '', name: '', sort: 0, enabled: true, remark: '' }
}

/** 新增项默认表单(dictTypeCode = 当前选中类型)。 */
export function blankItem(dictTypeCode = ''): DictItemInput {
  return { dictTypeCode, label: '', value: '', sort: 0, enabled: true }
}
