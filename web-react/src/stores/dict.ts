// 字典缓存(会话级内存缓存,不持久化——字典是服务端数据,进 localStorage 只会陈旧):
//   load 命中缓存直接回;进行中共享同一 Promise(并发去重);失败不写缓存、清 pending →
//   下次访问自然重试(被动重试,不做定时/退避)。字典管理页增删改后调 invalidate 失效。
// 三条规则逐字对齐 Vue 侧 `stores/dict.ts`。
import { useEffect } from 'react'
import { create } from 'zustand'
import { dictApi } from '@/api'
import type { DictItem } from '@/types/api'

// 并发去重表:模块级,不进 state(Promise 不该进 devtools/持久化序列化)。
const pending = new Map<string, Promise<DictItem[]>>()

export interface DictState {
  cache: Record<string, DictItem[]>
  load: (typeCode: string) => Promise<DictItem[]>
  /** 字典管理页增删改后调用;不传清全部。连同 pending 一起清,防失效后新 load 命中旧的在途请求。 */
  invalidate: (typeCode?: string) => void
}

export const useDictStore = create<DictState>()((set, get) => ({
  cache: {},
  load: async (typeCode) => {
    const hit = get().cache[typeCode]
    if (hit) return hit
    let p = pending.get(typeCode)
    if (!p) {
      const req: Promise<DictItem[]> = dictApi
        .items(typeCode)
        .then((items) => {
          // 失效竞态守卫:invalidate 已把本请求从 pending 摘除时,在途结果不得写回(旧数据)。
          if (pending.get(typeCode) === req) set((s) => ({ cache: { ...s.cache, [typeCode]: items } }))
          return items
        })
        .finally(() => {
          if (pending.get(typeCode) === req) pending.delete(typeCode)
        })
      pending.set(typeCode, req)
      p = req
    }
    return p
  },
  invalidate: (typeCode) => {
    if (typeCode) {
      set((s) => {
        const { [typeCode]: _drop, ...rest } = s.cache
        return { cache: rest }
      })
      pending.delete(typeCode)
    } else {
      set({ cache: {} })
      pending.clear()
    }
  },
}))

/**
 * 空态常量。**它不是防无限重渲染的那道闸** —— 这一点变异验过,别把功劳记错地方:
 *
 *   把 `?? EMPTY` 换成 `?? []`(兜底仍在选择器外)→ **全绿**,因为它本来就是对的。
 *   把兜底挪进选择器(`useDictStore((s) => s.cache[code] ?? [])`)→ **四条全红**,
 *   React 原话 "The result of getSnapshot should be cached to avoid an infinite loop"。
 *
 * 也就是说:起作用的是**兜底的位置**(选择器之外),不是用不用常量。zustand 每次渲染都跑选择器
 * 并与上次结果 `Object.is` 比对,选择器里现造数组 = 每次都"变了" = 无限重渲染;
 * 选择器只取 `s.cache[code]`(可能 undefined),比对的就是那个稳定引用。
 *
 * 那 `EMPTY` 还留着干什么:两个小收益。空态时 `items` 的引用跨渲染稳定,下游 `memo` 的子组件不会被
 * 白白叫醒;而 `Object.freeze` 挡住「谁在返回的数组上原地 `push`」—— 那会污染**所有**空态消费者,
 * 与 `stores/auth.ts` 里 `EMPTY()` 写成工厂防的是同一件事。返回类型因此是 `readonly`:
 * 那是这个意图的类型表达,不是为了迁就 freeze 而加的 cast。(与 `stores/auth.ts` 里"纯函数 + 细粒度 hook"是同一个坑的两种长相。)
 */
const EMPTY: readonly DictItem[] = Object.freeze([] as DictItem[])

/**
 * 触发加载并返回字典项;`typeCode` 变化自动重新加载(级联场景)。
 * 失败静默 —— 字典是配角,下拉留空不打断主流程;要提示的页面自己 `load().catch`。
 */
export function useDictOptions(typeCode: string): readonly DictItem[] {
  const items = useDictStore((s) => s.cache[typeCode])
  useEffect(() => {
    void useDictStore.getState().load(typeCode).catch(() => {})
  }, [typeCode])
  return items ?? EMPTY
}
