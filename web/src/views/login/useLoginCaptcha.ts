import { computed, getCurrentScope, onScopeDispose, readonly, shallowRef } from 'vue'
import { authApi } from '@/api'
import { translateError } from '@/utils/error'

/** 登录验证码票据：拥有加载、失败、一次性消费与重试状态。 */
export function useLoginCaptcha() {
  const enabled = shallowRef(false)
  const captchaId = shallowRef('')
  const captchaSvg = shallowRef('')
  const captchaCode = shallowRef('')
  const captchaType = shallowRef('char')
  const loading = shallowRef(false)
  const error = shallowRef('')
  const ready = computed(() => !enabled.value || (!!captchaId.value && !!captchaSvg.value))
  let requestVersion = 0

  async function load() {
    if (!enabled.value || loading.value) return
    const version = ++requestVersion
    loading.value = true
    error.value = ''
    captchaId.value = ''
    captchaSvg.value = ''
    try {
      const captcha = await authApi.captcha()
      if (!captcha.captchaId || !captcha.svg) throw new Error('Invalid captcha response')
      if (version !== requestVersion || !enabled.value) return
      captchaId.value = captcha.captchaId
      captchaSvg.value = captcha.svg
      captchaType.value = captcha.type || 'char'
    } catch (cause) {
      if (version === requestVersion && enabled.value) error.value = translateError(cause)
    } finally {
      if (version === requestVersion) loading.value = false
    }
  }

  async function configure(value: boolean) {
    requestVersion++
    loading.value = false
    enabled.value = value
    captchaCode.value = ''
    if (value) await load()
    else {
      captchaId.value = ''
      captchaSvg.value = ''
      error.value = ''
    }
  }

  async function refreshAfterUse() {
    if (!enabled.value) return
    captchaCode.value = ''
    await load()
  }

  function requestFields() {
    return enabled.value ? { captchaId: captchaId.value, captchaCode: captchaCode.value } : {}
  }

  if (getCurrentScope()) onScopeDispose(() => { requestVersion++ })

  return {
    enabled: readonly(enabled),
    captchaId: readonly(captchaId),
    captchaSvg: readonly(captchaSvg),
    captchaCode,
    captchaType: readonly(captchaType),
    loading: readonly(loading),
    error: readonly(error),
    ready,
    load,
    configure,
    refreshAfterUse,
    requestFields,
  }
}
