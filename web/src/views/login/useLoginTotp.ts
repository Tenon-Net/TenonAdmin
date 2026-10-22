import { reactive, readonly, shallowRef } from 'vue'
import { authApi } from '@/api'
import type { LoginOutput } from '@/types/api'

/** TOTP 二次验证挑战，独立拥有挑战票据、动态口令和请求状态。 */
export function useLoginTotp() {
  const challenge = reactive({ challengeId: '', code: '' })
  const loading = shallowRef(false)

  function start(challengeId: string) {
    challenge.challengeId = challengeId
    challenge.code = ''
  }

  function reset() {
    challenge.challengeId = ''
    challenge.code = ''
  }

  async function submit(): Promise<LoginOutput> {
    loading.value = true
    try {
      return await authApi.totpChallengeLogin({ challengeId: challenge.challengeId, code: challenge.code })
    } finally {
      loading.value = false
    }
  }

  return { challenge, loading: readonly(loading), start, reset, submit }
}
