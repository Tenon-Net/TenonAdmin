<script setup lang="ts">
// 外部登录 / SSO 回调结果页(批次 D)。IdP → 后端回调换会话后,后端 302 到本页并带:
//   ?ticket=xxx        登录成功:凭一次性票据换令牌 → 存会话 → 进首页
//   ?bind=code         绑定成功:回个人中心绑定页
//   ?pendingLink=…&provider=…  未绑定:转登录页,账密成功后认领自动绑定
//   ?totpChallenge=id (+ error=40018) SSO 后需 TOTP:带回登录页完成二次验证
//   ?error=NNNNN       失败:按错误码提示,稍候回登录页
// 公开路由(未登录也能到);令牌不进 URL,只带票据(见后端 ExternalAuthController)。
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { NSpin } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { externalAuthApi } from '@/api'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'
import { resetRouter } from '@/router'
import { translateError } from '@/utils/error'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const user = useUserStore()
const auth = useAuthStore()

const state = ref<'processing' | 'error'>('processing')
const errorText = ref('')

// 后端按 ErrorCode 数字码重定向;这里映射已知 oauth 码到 i18n(与后端 40013–40017 对齐)
const OAUTH_ERROR_KEYS: Record<number, string> = {
  40013: 'error.auth.oauthProviderDisabled',
  40014: 'error.auth.oauthStateInvalid',
  40015: 'error.auth.oauthExchangeFailed',
  40016: 'error.auth.oauthAccountNotBound',
  40017: 'error.auth.oauthAlreadyBound',
}

function fail(text: string) {
  state.value = 'error'
  errorText.value = text
  window.setTimeout(() => router.replace('/login'), 2600)
}

/**
 * 丢掉残留 SPA 会话。解绑后 SSO 会回 pendingLink/totpChallenge → /login;
 * 若 localStorage 仍有 token,路由守卫会把「已登录访问 /login」弹回首页,看起来像「GitHub 直接登录进去了」。
 * 双前端(5173/5174)不同源存储时尤其易踩:在一端登出,回调却落到另一端仍带旧会话。
 */
function dropResidualSession() {
  if (!user.accessToken && !user.refreshToken && !user.cookieSession) return
  resetRouter()
  auth.reset()
  user.clear()
}

onMounted(async () => {
  const q = route.query
  const ticket = typeof q.ticket === 'string' ? q.ticket : ''
  const bind = typeof q.bind === 'string' ? q.bind : ''
  const pendingLink = typeof q.pendingLink === 'string' ? q.pendingLink : ''
  const provider = typeof q.provider === 'string' ? q.provider : ''
  const displayName = typeof q.displayName === 'string' ? q.displayName : ''
  const error = typeof q.error === 'string' ? q.error : ''
  const totpChallenge = typeof q.totpChallenge === 'string' ? q.totpChallenge : ''

  // SSO 已通过 IdP,但需 TOTP:把挑战 Id 交给登录页完成 40018 流
  if (totpChallenge) {
    dropResidualSession()
    await router.replace({ path: '/login', query: { totpChallenge } })
    return
  }

  // 未绑定:转登录页现场绑定(账密成功后须确认再 claim);必须先清残留会话,否则守卫直接送首页
  if (pendingLink) {
    dropResidualSession()
    await router.replace({
      path: '/login',
      query: {
        pendingLink,
        ...(provider ? { provider } : {}),
        ...(displayName ? { displayName } : {}),
      },
    })
    return
  }

  if (error) {
    const key = OAUTH_ERROR_KEYS[Number(error)]
    fail(key ? t(key) : t('oauth.failed'))
    return
  }

  if (bind) {
    router.replace('/personal/bindings') // 绑定成功回个人中心
    return
  }

  if (ticket) {
    try {
      user.setSession(await externalAuthApi.exchange(ticket))
      router.replace('/')
    } catch (e) {
      fail(translateError(e))
    }
    return
  }

  fail(t('oauth.failed')) // 既无票据也无错误码:异常回退
})
</script>

<template>
  <div class="oauth-cb">
    <div v-if="state === 'processing'" class="oauth-box">
      <n-spin size="large" />
      <p class="oauth-msg">{{ t('oauth.processing') }}</p>
    </div>
    <div v-else class="oauth-box">
      <Icon icon="ph:warning-circle-duotone" class="oauth-err-icon" />
      <p class="oauth-msg">{{ errorText }}</p>
      <p class="oauth-sub">{{ t('oauth.backToLogin') }}</p>
    </div>
  </div>
</template>

<style scoped>
.oauth-cb {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--color-bg-layout, #f5f6f8);
}
.oauth-box {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 14px;
}
.oauth-msg {
  font-size: 15px;
  color: var(--color-text-primary);
}
.oauth-sub {
  font-size: 13px;
  color: var(--color-text-tertiary);
}
.oauth-err-icon {
  font-size: 44px;
  color: var(--color-error, #e88080);
}
</style>
