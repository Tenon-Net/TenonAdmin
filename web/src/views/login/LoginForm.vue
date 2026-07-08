<script setup lang="ts">
import { reactive, ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { NForm, NFormItem, NInput, NCheckbox, useMessage } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { authApi } from '@/api'
import { useUserStore } from '@/stores/user'
import { useAppStore } from '@/stores/app'
import { btnGrad, glowSh } from '@/theme/mix'
import { translateError } from '@/utils/error'
import TenonLogo from '@/components/TenonLogo.vue'

// 皮肤外壳自带品牌栏时(如双栏),不再在卡内重复 logo;自带问候语时(双栏右栏)也不重复标题。
withDefaults(defineProps<{ showLogo?: boolean; showTitle?: boolean }>(), { showLogo: true, showTitle: true })

const router = useRouter()
const message = useMessage()
const { t } = useI18n()
const user = useUserStore()
const app = useAppStore()

// ponytail: 开发环境预填超管账号密码,免得每次手敲;生产(build)下 import.meta.env.DEV 为 false,留空
const model = reactive(
  import.meta.env.DEV
    ? { account: 'superAdmin', password: 'Aa123456', remember: true }
    : { account: '', password: '', remember: true },
)
const loading = ref(false)

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
    message.warning(t('login.passwordPlaceholder'))
    return
  }
  loading.value = true
  try {
    const res = await authApi.login({ account: model.account, password: model.password })
    user.setSession(res)
    message.success(t('login.success'))
    router.replace('/')
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
      <TenonLogo :size="34" />
      <span class="lf-word">TenonAdmin</span>
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
      <div class="row">
        <n-checkbox v-model:checked="model.remember">{{ t('login.remember') }}</n-checkbox>
      </div>
      <button class="hero-btn" :style="heroStyle" :disabled="loading" @click.prevent="onSubmit">
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
</style>
