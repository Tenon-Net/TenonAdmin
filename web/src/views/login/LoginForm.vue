<script setup lang="ts">
import { reactive, ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRouter } from 'vue-router'
import { NForm, NFormItem, NInput, NCheckbox, useMessage } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { authApi, externalAuthApi, ApiError } from '@/api'
import type { LoginOutput } from '@/types/api'
import { useUserStore } from '@/stores/user'
import { useAppStore } from '@/stores/app'
import { useSite } from '@/composables/useSite'
import { btnGrad, glowSh } from '@/theme/mix'
import { translateError } from '@/utils/error'
import TenonLogo from '@/components/TenonLogo.vue'

// 皮肤外壳自带品牌栏时(如双栏),不再在卡内重复 logo/标题/页脚(showFooter=false 由外壳自绘版权)。
withDefaults(defineProps<{ showLogo?: boolean; showTitle?: boolean; showFooter?: boolean }>(), {
  showLogo: true,
  showTitle: true,
  showFooter: true,
})

const router = useRouter()
const message = useMessage()
const { t } = useI18n()
const user = useUserStore()
const app = useAppStore()
const { site, appVersion, loadSite } = useSite()
const year = new Date().getFullYear()

// ponytail: 开发环境预填超管账号密码,免得每次手敲;生产(build)下 import.meta.env.DEV 为 false,留空
const model = reactive(
  import.meta.env.DEV
    ? { account: 'superAdmin', password: 'Aa123456', remember: true }
    : { account: '', password: '', remember: true },
)
const loading = ref(false)

// 登录形态:账号密码 / 短信免密(sys.security.smsLogin.enabled 运行时驱动)/ 短信二次验证(密码过后 40009 信令进入)
const mode = ref<'account' | 'sms' | 'mfa'>('account')

// 短信免密登录
const smsModel = reactive({ phone: '', code: '' })
// 二次验证挑战(40009 信令 args 下发)
const mfa = reactive({ challengeId: '', phoneMask: '', code: '' })

// 发码/重发共用倒计时(同一时刻只有一个发码入口可见)
const countdown = ref(0)
let timer: number | undefined
function startCountdown(s: number) {
  countdown.value = s
  window.clearInterval(timer)
  timer = window.setInterval(() => {
    countdown.value -= 1
    if (countdown.value <= 0) window.clearInterval(timer)
  }, 1000)
}
onBeforeUnmount(() => window.clearInterval(timer))

// 验证码:是否启用由匿名站点信息(sys.security.captcha.enabled)运行时驱动;启用才拉取并展示。
// 账号模式护登录,短信模式护发码(发码端点防脚本滥用)。
const captchaEnabled = ref(false)
const captchaId = ref('')
const captchaSvg = ref('')
const captchaCode = ref('')
const captchaType = ref('char')

// math 类型需算出结果再输入,提示语不同;其余类型统一“请输入验证码”。
const captchaHint = computed(() =>
  captchaType.value === 'math' ? t('login.captchaMathPlaceholder') : t('login.captchaPlaceholder'),
)

async function loadCaptcha() {
  try {
    const c = await authApi.captcha()
    captchaId.value = c.captchaId
    captchaSvg.value = c.svg
    captchaType.value = c.type || 'char'
  } catch {
    // 拉取失败不阻塞登录页渲染;点击图形可重试
  }
}

/** 验证码一次性消费:任何用掉票据的请求失败后必刷新,避免复用作废票据 */
async function refreshCaptchaAfterUse() {
  if (captchaEnabled.value) {
    captchaCode.value = ''
    await loadCaptcha()
  }
}

onMounted(async () => {
  // 站点信息全站共用(useSite 去重);验证码开关据此运行时驱动,启用才拉图。
  await loadSite()
  if (site.captchaEnabled) {
    captchaEnabled.value = true
    await loadCaptcha()
  }
  void loadSsoProviders()
})

// 英雄按钮:accent 派生渐变 + 发光(仅登录页/英雄区)。
const heroStyle = computed(() => ({ background: btnGrad(app.accent), boxShadow: glowSh(app.accent) }))

// 第三方登录:后端 GET providers 驱动(仅点亮已启用的)。点击顶层导航到 authorize,后端 302 跳 IdP。
const ssoProviders = ref<{ code: string; displayName: string; icon?: string | null }[]>([])
async function loadSsoProviders() {
  try {
    ssoProviders.value = await externalAuthApi.providers()
  } catch {
    // 未配置外部登录或拉取失败:整段 SSO 区不显
  }
}
function onSso(code: string) {
  window.location.href = externalAuthApi.authorizeUrl(code)
}

function finishLogin(res: LoginOutput) {
  user.setSession(res)
  message.success(t('login.success'))
  router.replace('/')
}

async function onSubmit() {
  if (mode.value === 'mfa') return onMfaSubmit()
  if (mode.value === 'sms') return onSmsSubmit()

  if (!model.account || !model.password) {
    message.warning(t('login.required'))
    return
  }
  if (captchaEnabled.value && !captchaCode.value) {
    message.warning(t('login.captchaRequired'))
    return
  }
  loading.value = true
  try {
    const res = await authApi.login({
      account: model.account,
      password: model.password,
      ...(captchaEnabled.value ? { captchaId: captchaId.value, captchaCode: captchaCode.value } : {}),
    })
    finishLogin(res)
  } catch (e) {
    // 40009 = 密码已过、需短信二次验证(信令而非失败):切码输入页,args 带挑战与倒计时参数
    if (e instanceof ApiError && e.code === 40009 && e.args) {
      mfa.challengeId = String(e.args.challengeId ?? '')
      mfa.phoneMask = String(e.args.phoneMask ?? '')
      mfa.code = ''
      mode.value = 'mfa'
      startCountdown(Number(e.args.resendSeconds ?? 60))
    } else {
      message.error(translateError(e))
    }
    await refreshCaptchaAfterUse()
  } finally {
    loading.value = false
  }
}

async function onMfaSubmit() {
  if (!mfa.code) {
    message.warning(t('login.smsCodePlaceholder'))
    return
  }
  loading.value = true
  try {
    finishLogin(await authApi.smsChallengeLogin({ challengeId: mfa.challengeId, code: mfa.code }))
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

async function onMfaResend() {
  if (countdown.value > 0) return
  try {
    const r = await authApi.smsChallengeResend({ challengeId: mfa.challengeId })
    message.success(t('login.smsSent'))
    startCountdown(r.resendSeconds)
  } catch (e) {
    message.error(translateError(e))
  }
}

function backToAccount() {
  mode.value = 'account'
  mfa.challengeId = ''
  mfa.code = ''
}

async function onSendSmsCode() {
  if (countdown.value > 0) return
  if (!smsModel.phone) {
    message.warning(t('login.phonePlaceholder'))
    return
  }
  if (captchaEnabled.value && !captchaCode.value) {
    message.warning(t('login.captchaRequired'))
    return
  }
  try {
    const r = await authApi.smsLoginSend({
      phone: smsModel.phone,
      ...(captchaEnabled.value ? { captchaId: captchaId.value, captchaCode: captchaCode.value } : {}),
    })
    message.success(t('login.smsSent'))
    startCountdown(r.resendSeconds)
  } catch (e) {
    message.error(translateError(e))
  }
  await refreshCaptchaAfterUse()
}

async function onSmsSubmit() {
  if (!smsModel.phone || !smsModel.code) {
    message.warning(t('login.smsRequired'))
    return
  }
  loading.value = true
  try {
    finishLogin(await authApi.smsLogin({ phone: smsModel.phone, code: smsModel.code }))
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-form">
    <div v-if="showLogo" class="lf-brand">
      <!-- 站点 logo:后台配了图片地址用 <img>,否则回退内置矢量 logo(留空即回退) -->
      <img v-if="site.logo" :src="site.logo" :alt="site.title" class="lf-logo" />
      <TenonLogo v-else :size="34" />
      <span class="lf-word">{{ site.title }}</span>
    </div>

    <!-- 短信二次验证:密码已过,凭挑战 + 短信码完成登录 -->
    <template v-if="mode === 'mfa'">
      <h2 v-if="showTitle" class="lf-title">{{ t('login.mfaTitle', { phone: mfa.phoneMask }) }}</h2>
      <p class="lf-hint-line">{{ t('login.mfaSub') }}</p>
      <n-form @keyup.enter="onSubmit">
        <n-form-item :label="t('login.smsCode')" path="code">
          <n-input v-model:value="mfa.code" :placeholder="t('login.smsCodePlaceholder')" size="large" :maxlength="6">
            <template #prefix><Icon icon="ph:chat-circle-text" /></template>
          </n-input>
        </n-form-item>
        <div class="row lf-between">
          <a class="lf-link" @click="backToAccount">{{ t('login.backToPassword') }}</a>
          <a class="lf-link" :class="{ 'lf-link-disabled': countdown > 0 }" @click="onMfaResend">
            {{ countdown > 0 ? t('login.resendAfter', { s: countdown }) : t('login.sendCode') }}
          </a>
        </div>
        <button class="hero-btn" type="button" :style="heroStyle" :disabled="loading" @click.prevent="onSubmit">
          {{ loading ? t('common.loading') : t('login.submit') }}
        </button>
      </n-form>
    </template>

    <template v-else>
      <h2 v-if="showTitle" class="lf-title">{{ t('login.title') }}</h2>
      <n-form :model="mode === 'account' ? model : smsModel" @keyup.enter="onSubmit">
        <template v-if="mode === 'account'">
          <n-form-item :label="t('login.account')" path="account">
            <n-input v-model:value="model.account" :placeholder="t('login.accountPlaceholder')" size="large">
              <template #prefix><Icon icon="ph:user" /></template>
            </n-input>
          </n-form-item>
          <n-form-item :label="t('login.password')" path="password">
            <n-input
              v-model:value="model.password"
              type="password"
              show-password-on="click"
              :placeholder="t('login.passwordPlaceholder')"
              size="large"
            >
              <template #prefix><Icon icon="ph:lock" /></template>
            </n-input>
          </n-form-item>
        </template>
        <template v-else>
          <n-form-item :label="t('login.phone')" path="phone">
            <n-input v-model:value="smsModel.phone" :placeholder="t('login.phonePlaceholder')" size="large" :maxlength="20">
              <template #prefix><Icon icon="ph:device-mobile" /></template>
            </n-input>
          </n-form-item>
        </template>
        <n-form-item v-if="captchaEnabled" :label="t('login.captcha')" path="captcha">
          <div class="lf-captcha">
            <n-input
              v-model:value="captchaCode"
              :placeholder="captchaHint"
              size="large"
              @keyup.enter="onSubmit"
            >
              <template #prefix><Icon icon="ph:shield-check" /></template>
            </n-input>
            <!-- SVG 来自本站后端;点击重取一张(一次性票据) -->
            <button type="button" class="lf-captcha-img" :title="t('login.captchaPlaceholder')" @click="loadCaptcha" v-html="captchaSvg" />
          </div>
        </n-form-item>
        <n-form-item v-if="mode === 'sms'" :label="t('login.smsCode')" path="code">
          <div class="lf-captcha">
            <n-input v-model:value="smsModel.code" :placeholder="t('login.smsCodePlaceholder')" size="large" :maxlength="6">
              <template #prefix><Icon icon="ph:chat-circle-text" /></template>
            </n-input>
            <button type="button" class="lf-send-btn" :disabled="countdown > 0" @click="onSendSmsCode">
              {{ countdown > 0 ? t('login.resendAfter', { s: countdown }) : t('login.sendCode') }}
            </button>
          </div>
        </n-form-item>
        <div class="row lf-between">
          <n-checkbox v-if="mode === 'account'" v-model:checked="model.remember">{{ t('login.remember') }}</n-checkbox>
          <span v-else />
          <a v-if="site.smsLoginEnabled" class="lf-link" @click="mode = mode === 'account' ? 'sms' : 'account'">
            {{ mode === 'account' ? t('login.smsLogin') : t('login.accountLogin') }}
          </a>
        </div>
        <button class="hero-btn" type="button" :style="heroStyle" :disabled="loading" @click.prevent="onSubmit">
          {{ loading ? t('common.loading') : t('login.submit') }}
        </button>
      </n-form>

      <!-- 第三方登录:后端 providers 驱动;无启用项则整段不显。点击顶层跳转到 IdP(OAuth2 授权码往返)。 -->
      <template v-if="ssoProviders.length">
        <div class="lf-divider"><span>{{ t('login.otherMethods') }}</span></div>
        <div class="lf-sso">
          <button v-for="p in ssoProviders" :key="p.code" class="lf-sso-btn" type="button" @click="onSso(p.code)">
            <Icon v-if="p.icon" :icon="p.icon" class="lf-sso-icon" />
            {{ p.displayName }}
          </button>
        </div>
      </template>
    </template>

    <!-- 页脚:版权(可选链接)+ 构建期版本号。皮肤自绘页脚时(如双栏)传 :show-footer="false" 关掉。 -->
    <footer v-if="showFooter" class="lf-foot">
      <span>
        © {{ year }}
        <a v-if="site.copyrightUrl" :href="site.copyrightUrl" target="_blank" rel="noopener">{{ site.copyright || site.title }}</a>
        <template v-else>{{ site.copyright || site.title }}</template>
      </span>
      <span v-if="appVersion" class="lf-ver">v{{ appVersion }}</span>
    </footer>
  </div>
</template>

<style scoped>
/* 文字色默认跟随应用令牌;皮肤可通过 --lf-title / --lf-hint 覆盖(如极光深色皮肤强制浅色)。 */
.login-form {
  width: 100%;
}
.lf-brand {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 18px;
}
/* 后台配置的品牌 logo:限高与内置矢量 logo 齐平,宽度自适应不变形 */
.lf-logo {
  height: 34px;
  width: auto;
  object-fit: contain;
  display: block;
}
.lf-word {
  font-size: 18px;
  font-weight: 700;
  letter-spacing: 0.2px;
  color: var(--lf-title, var(--color-text-primary));
}
.lf-title {
  font-size: var(--font-size-lg);
  font-weight: 600;
  margin: 0 0 22px;
  color: var(--lf-title, var(--color-text-primary));
}
/* 二次验证副标题:标题下的弱化说明行 */
.lf-hint-line {
  margin: -14px 0 18px;
  font-size: 13px;
  color: var(--lf-hint, var(--color-text-tertiary));
}
.row {
  margin: 4px 0 20px;
}
.lf-between {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
/* 模式切换/重发:文字链接,复用主题色 */
.lf-link {
  font-size: 13px;
  color: var(--color-primary);
  cursor: pointer;
  user-select: none;
}
.lf-link:hover {
  opacity: 0.85;
}
.lf-link-disabled {
  color: var(--lf-hint, var(--color-text-tertiary));
  cursor: default;
  pointer-events: none;
}
/* 验证码:输入框 + 可点击刷新的 SVG 图形(等高对齐) */
.lf-captcha {
  display: flex;
  gap: 10px;
  width: 100%;
  align-items: stretch;
}
.lf-captcha-img {
  flex: 0 0 auto;
  height: 40px;
  min-width: 96px;
  padding: 0;
  border: 1px solid var(--lf-border, var(--color-border));
  border-radius: var(--radius-md);
  background: #fff;
  cursor: pointer;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
}
.lf-captcha-img :deep(svg) {
  height: 100%;
  width: auto;
}
/* 发码按钮:与验证码图形同尺寸位,文字化 */
.lf-send-btn {
  flex: 0 0 auto;
  height: 40px;
  min-width: 96px;
  padding: 0 12px;
  border: 1px solid var(--lf-border, var(--color-border));
  border-radius: var(--radius-md);
  background: var(--color-fill);
  font-size: 13px;
  color: var(--lf-title, var(--color-text-secondary));
  cursor: pointer;
  transition:
    border-color var(--transition-fast),
    color var(--transition-fast);
}
.lf-send-btn:hover:not(:disabled) {
  border-color: var(--color-primary);
  color: var(--color-primary);
}
.lf-send-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
.hero-btn {
  width: 100%;
  height: 48px;
  border: none;
  border-radius: var(--radius-md);
  color: #fff;
  font-size: var(--font-size-md);
  font-weight: 600;
  cursor: pointer;
  transition:
    transform var(--transition-fast),
    box-shadow var(--transition-fast);
}
.hero-btn:hover {
  transform: translateY(-2px);
}
.hero-btn:active {
  transform: translateY(0);
}
.hero-btn:disabled {
  opacity: 0.7;
  cursor: not-allowed;
}
/* 第三方登录:分隔线 + 等宽按钮(设计稿 §Variant A) */
.lf-divider {
  display: flex;
  align-items: center;
  gap: 14px;
  margin: 24px 0 16px;
}
.lf-divider::before,
.lf-divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: var(--lf-border, var(--color-border));
}
.lf-divider span {
  font-size: 12px;
  color: var(--lf-hint, var(--color-text-tertiary));
}
.lf-sso {
  display: flex;
  gap: 12px;
}
.lf-sso-btn {
  flex: 1;
  height: 44px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  border: 1.5px solid var(--lf-border, var(--color-border));
  border-radius: 11px;
  background: var(--color-fill);
  font-size: 13px;
  color: var(--lf-title, var(--color-text-secondary));
  cursor: pointer;
  transition:
    border-color var(--transition-fast),
    color var(--transition-fast),
    background var(--transition-fast);
}
.lf-sso-btn:hover {
  border-color: var(--color-primary);
  color: var(--color-primary);
  background: var(--color-primary-light);
}
.lf-sso-btn:focus-visible {
  outline: 2px solid var(--color-primary);
  outline-offset: 2px;
}
/* 页脚:版权 + 版本号,弱化色,与 SSO 区留白 */
.lf-foot {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  margin-top: 24px;
  font-size: 12px;
  color: var(--lf-hint, var(--color-text-tertiary));
}
.lf-foot a {
  color: inherit;
  text-decoration: none;
}
.lf-foot a:hover {
  color: var(--color-primary);
}
.lf-ver {
  opacity: 0.75;
}
</style>
