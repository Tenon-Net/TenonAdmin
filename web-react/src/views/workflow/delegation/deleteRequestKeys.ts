import { useRequestKey, type RequestKeyOutcome } from '@/workflow/useRequestKey'
import type { WfId } from '@/workflow/id'

/** 每条委托规则独立维护删除请求键,避免并发删除或重试串键。 */
export function createDeleteRequestKeys() {
  const keys = new Map<string, ReturnType<typeof useRequestKey>>()

  const keyFor = (id: WfId) => {
    const canonical = String(id)
    let key = keys.get(canonical)
    if (!key) {
      key = useRequestKey()
      keys.set(canonical, key)
    }
    return key
  }

  return {
    value: (id: WfId) => keyFor(id).value(),
    settle: (id: WfId, outcome: RequestKeyOutcome) => {
      const canonical = String(id)
      const key = keys.get(canonical)
      key?.settle(outcome)
      if (outcome === 'success') keys.delete(canonical)
    },
  }
}
