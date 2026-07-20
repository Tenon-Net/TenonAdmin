import type { ReactNode } from 'react'
import { useHasPerm } from '@/stores/auth'

/**
 * 按钮级权限门。替代 Vue 侧的 `v-auth` 指令(React 没有指令,用组件包裹)。
 *   <Can code="POST:/api/v1/sys/user"><Button>新增</Button></Can>   单码
 *   <Can code={['a','b']}>…</Can>                                    多码,默认 OR(some)
 *   <Can code={['a','b']} every>…</Can>                              多码,AND(every)
 *
 * 判定规则(超管 fail-open / 未加载 fail-closed / 精确命中)**收敛在 `auth.hasPerm`**,这里不重写 ——
 * 与 Vue 侧 `v-auth` 走同一个 `hasPerm`,两个模板判定一致。`useHasPerm` 是细粒度订阅(只订权限相关字段)。
 *
 * 空数组沿用 JS 语义:`[].some`=false(OR 隐藏)、`[].every`=true(AND 显示)——与 Vue `v-auth` 一致,
 * 但别依赖它设计权限,那是边界巧合不是意图。**服务端始终是权威**,这里只拨显隐、不谎报。
 */
export function Can({ code, every = false, children }: { code: string | string[]; every?: boolean; children: ReactNode }) {
  const has = useHasPerm()
  const codes = Array.isArray(code) ? code : [code]
  const ok = every ? codes.every(has) : codes.some(has)
  return ok ? <>{children}</> : null
}
