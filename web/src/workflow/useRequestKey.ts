// 一次用户动作(打开弹窗/点一次提交)期间复用同一个 requestId,配后端 operation receipt 幂等。
//   value()   —— 惰性生成:首次调用才 crypto.randomUUID(),之后重复调用返回同一个值,直到 settle/reset。
//   settle(o) —— 'success' | 'error' 立刻丢弃(下次 value() 换新);'network' 保留(重试要复用同一个 key,
//                 让服务端的回执兜住"其实已经成功了"的情况——这是 receipt 存在的意义,不是本 composable 发明的)。
//   reset()   —— 无条件丢弃,给"打开一个新动作"用(不依赖 settle 是否发生过)。
// 页面级 composable,不是 store:请求键的生命周期天然绑定单个页面/单个弹窗实例,没有跨组件共享的需求。
import { ref } from 'vue'
import { ApiError } from '@/api'

export type RequestKeyOutcome = 'success' | 'error' | 'network'

function generateId(): string {
  // crypto.randomUUID 需要安全上下文(HTTPS/localhost);仓内 utils/chunkUpload.ts 的 crypto.subtle.digest
  // 已经默许这个前提。极端情况下(非安全上下文)退化成时间戳 + 随机数拼接,不静默失败、不引入 uuid 包。
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}`
}

export function useRequestKey() {
  const key = ref<string | null>(null)

  function value(): string {
    if (key.value == null) key.value = generateId()
    return key.value
  }

  function settle(outcome: RequestKeyOutcome) {
    if (outcome !== 'network') key.value = null
  }

  function reset() {
    key.value = null
  }

  return { value, settle, reset }
}

/** 网络层未拿到确定结果(断网/超时/CORS)才算 'network';已结算的 HTTP 响应(含业务失败)一律 'error'。 */
export function classifyOutcome(e: unknown): 'error' | 'network' {
  return e instanceof ApiError ? 'error' : 'network'
}
