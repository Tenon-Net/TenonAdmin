// 模块(应用)页纯逻辑:内置保护判定 + 全量入参映射 + 默认表单。抽出做变异钉,index.tsx 只接线。
import type { ModuleInput, ModuleRow } from '@/types/api'

/** 内置 system 模块受保护(后端 42013 禁删):前端据 code 禁删 + 禁停,免明知不可为的请求。变异钉。 */
export function isBuiltin(r: Pick<ModuleRow, 'code'>): boolean {
  return r.code === 'system'
}

/**
 * 行 → 全量入参:openEdit 回填与 StatusSwitch 行内改状态**共用**——后端无独立启停端点,均走全量 update,
 * 漏一字段就把该字段抹空,故逐字段带全(可空字段归一空串)。变异钉。
 */
export function moduleToInput(r: ModuleRow): ModuleInput {
  return {
    code: r.code, title: r.title, icon: r.icon ?? '', defaultRoute: r.defaultRoute ?? '',
    apiPrefix: r.apiPrefix ?? '', sort: r.sort, enabled: r.enabled, remark: r.remark ?? '',
  }
}

/** 新增默认表单(全字段空串 / sort 0 / 启用)。 */
export function blankModule(): ModuleInput {
  return { code: '', title: '', icon: '', defaultRoute: '', apiPrefix: '', sort: 0, enabled: true, remark: '' }
}
