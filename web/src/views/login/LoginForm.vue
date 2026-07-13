<script setup lang="ts">
import { reactive, ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { NForm, NFormItem, NInput, NCheckbox, useMessage } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { authApi } from '@/api'
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

// 验证码:是否启用由匿名站点信息(sys.security.captcha.enabled)运行时驱动;启用才拉取并展示。
const captchaEnabled = ref(false)
const captchaId = ref('')
const captchaSvg = ref('')
const captchaCode = ref('')

async function loadCaptcha() {
  try {
    const c = await authApi.captcha()
    captchaId.value = c.captchaId
    captchaSvg.value = c.svg
  } catch {
    // 拉取失败不阻塞登录页渲染;点击图形可重试
  }
}

onMounted(async () => {
  // 站点信息全站共用(useSite 去重);验证码开关据此运行时驱动,启用才拉图。
  await loadSite()
  if (site.captchaEnabled) {
    captchaEnabled.value = true
    await loadCaptcha()
  }
})

// 英雄按钮:accent 派生渐变 + 发光(仅登录页/英雄区)。
const heroStyle = computed(() => ({ background: btnGrad(app.accent), boxShadow: glowSh(app.accent) }))

// 第三方登录:设计稿定义为 企业微信 / 钉钉 / SSO。功能待接入,先占位显示。
const ssoList = [
  { key: 'wecom', label: 'login.ssoWecom' },
  { key: 'dingtalk', label: 'login.ssoDingtalk' },
  { key: 'sso', label: 'login.sso' },
]
function onSso() {
  message.info(t('login.ssoComingSoon'))
}

async function onSubmit() {
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
    user.setSession(res)
    message.success(t('login.success'))
    router.replace('/')
  } catch (e) {
    message.error(translateError(e))
    // 验证码一次性消费:登录失败后必刷新,避免复用作废票据
    if (captchaEnabled.value) {
      captchaCode.value = ''
      await loadCaptcha()
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-form">
    <div v-if="showLogo" class="lf-brand">
      <TenonLogo :size="34" />
      <span class="lf-word">{{ site.title }}</span>
    </div>
    <h2 v-if="showTitle" class="lf-title">{{ t('login.title') }}</h2>
    <n-form :model="model" @keyup.enter="onSubmit">
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
      <n-form-item v-if="captchaEnabled" :label="t('login.captcha')" path="captcha">
        <div class="lf-captcha">
          <n-input
            v-model:value="captchaCode"
            :placeholder="t('login.captchaPlaceholder')"
            size="large"
            @keyup.enter="onSubmit"
          >
            <template #prefix><Icon icon="ph:shield-check" /></template>
          </n-input>
          <!-- SVG 来自本站后端;点击重取一张(一次性票据) -->
          <button type="button" class="lf-captcha-img" :title="t('login.captchaPlaceholder')" @click="loadCaptcha" v-html="captchaSvg" />
        </div>
      </n-form-item>
      <div class="row">
        <n-checkbox v-model:checked="model.remember">{{ t('login.remember') }}</n-checkbox>
      </div>
      <button class="hero-btn" type="button" :style="heroStyle" :disabled="loading" @click.prevent="onSubmit">
        {{ loading ? t('common.loading') : t('login.submit') }}
      </button>
    </n-form>

    <!-- 第三方登录(占位:先放按钮,不接功能)。文案/顺序依设计稿:企业微信 / 钉钉 / SSO。 -->
    <div class="lf-divider"><span>{{ t('login.otherMethods') }}</span></div>
    <div class="lf-sso">
      <button v-for="s in ssoList" :key="s.key" class="lf-sso-btn" type="button" @click="onSso">
        {{ t(s.label) }}
      </button>
    </div>

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
.row {
  margin: 4px 0 20px;
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
