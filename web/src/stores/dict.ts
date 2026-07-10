// 字典缓存(会话级内存缓存,不持久化——字典是服务端数据,进 localStorage 只会陈旧):
//   load 命中缓存直接回;进行中共享同一 Promise(并发去重);失败不写缓存、清 pending →
//   下次访问自然重试(被动重试,不做定时/退避)。字典管理页增删改后调 invalidate 失效。
import { computed, toValue, watchEffect, type ComputedRef, type MaybeRefOrGetter } from 'vue'
import { defineStore } from 'pinia'
import { dictApi } from '@/api'
import type { DictItem } from '@/types/api'

// 并发去重表:模块级,不进 state(Promise 不该进 devtools/持久化序列化)。
const pending = new Map<string, Promise<DictItem[]>>()

export const useDictStore = defineStore('dict', {
  state: () => ({ cache: {} as Record<string, DictItem[]> }),
  actions: {
    async load(typeCode: string): Promise<DictItem[]> {
      const hit = this.cache[typeCode]
      if (hit) return hit
      let p = pending.get(typeCode)
      if (!p) {
        const req: Promise<DictItem[]> = dictApi
          .items(typeCode)
          .then((items) => {
            // 失效竞态守卫:invalidate 已把本请求从 pending 摘除时,在途结果不得写回(旧数据)。
            if (pending.get(typeCode) === req) this.cache[typeCode] = items
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
    /** 字典管理页增删改后调用;不传清全部。连同 pending 一起清,防失效后新 load 命中旧的在途请求。 */
    invalidate(typeCode?: string) {
      if (typeCode) {
        delete this.cache[typeCode]
        pending.delete(typeCode)
      } else {
        this.cache = {}
        pending.clear()
      }
    },
  },
})

/** setup 糖:触发加载并返回响应式字典项;typeCode 可为 ref/getter(级联场景变化自动加载)。失败静默——字典是配角,下拉留空不打断主流程;要提示的页面自己 load().catch。 */
export function useDictOptions(typeCode: MaybeRefOrGetter<string>): ComputedRef<DictItem[]> {
  const store = useDictStore()
  watchEffect(() => {
    void store.load(toValue(typeCode)).catch(() => {})
  })
  return computed(() => store.cache[toValue(typeCode)] ?? [])
}
