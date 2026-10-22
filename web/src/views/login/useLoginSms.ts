import { onBeforeUnmount, reactive, readonly, shallowRef } from 'vue'
import { authApi } from '@/api'
import type { LoginOutput } from '@/types/api'

interface LoginCaptchaFields {
  captchaId?: string
  captchaCode?: string
}

/** 短信免密与密码后的短信挑战共享同一个倒计时，但各自保留独立表单状态。 */
export function useLoginSms() {
  const smsModel = reactive({ phone: '', code: '' })
  const challenge = reactive({ challengeId: '', phoneMask: '', code: '' })
  const countdown = shallowRef(0)
  const loading = shallowRef(false)
  let timer: number | undefined
  let countdownRequestVersion = 0
  let disposed = false

  function startCountdown(seconds: number) {
    if (disposed) return
    window.clearInterval(timer)
    countdown.value = Math.max(0, seconds)
    if (countdown.value === 0) return
    timer = window.setInterval(() => {
      countdown.value = Math.max(0, countdown.value - 1)
      if (countdown.value === 0) window.clearInterval(timer)
    }, 1000)
  }

  function startChallenge(value: { challengeId: string; phoneMask: string; resendSeconds: number }) {
    challenge.challengeId = value.challengeId
    challenge.phoneMask = value.phoneMask
    challenge.code = ''
    startCountdown(value.resendSeconds)
  }

  function resetChallenge() {
    challenge.challengeId = ''
    challenge.phoneMask = ''
    challenge.code = ''
  }

  async function sendLoginCode(captcha: LoginCaptchaFields) {
    const version = ++countdownRequestVersion
    const result = await authApi.smsLoginSend({ phone: smsModel.phone, ...captcha })
    if (disposed || version !== countdownRequestVersion) return
    startCountdown(result.resendSeconds)
  }

  async function resendChallenge() {
    const version = ++countdownRequestVersion
    const result = await authApi.smsChallengeResend({ challengeId: challenge.challengeId })
    if (disposed || version !== countdownRequestVersion) return
    startCountdown(result.resendSeconds)
  }

  async function submitLogin(): Promise<LoginOutput> {
    loading.value = true
    try {
      return await authApi.smsLogin({ phone: smsModel.phone, code: smsModel.code })
    } finally {
      loading.value = false
    }
  }

  async function submitChallenge(): Promise<LoginOutput> {
    loading.value = true
    try {
      return await authApi.smsChallengeLogin({ challengeId: challenge.challengeId, code: challenge.code })
    } finally {
      loading.value = false
    }
  }

  onBeforeUnmount(() => {
    disposed = true
    countdownRequestVersion++
    window.clearInterval(timer)
  })

  return {
    smsModel,
    challenge,
    countdown: readonly(countdown),
    loading: readonly(loading),
    startChallenge,
    resetChallenge,
    sendLoginCode,
    resendChallenge,
    submitLogin,
    submitChallenge,
  }
}
