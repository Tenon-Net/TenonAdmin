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

const router = useRouter()
const message = useMessage()
const { t } = useI18n()
const user = useUserStore()
const app = useAppStore()

const model = reactive({ account: '', password: '', remember: true })
const loading = ref(false)

// 英雄按钮:accent 派生渐变 + 发光(仅登录页/英雄区)。
const heroStyle = computed(() => ({ background: btnGrad(app.accent), boxShadow: glowSh(app.accent) }))

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
  <div class="login">
    <!-- 左:品牌面板(窄屏隐藏)-->
    <div class="panel">
      <div class="glow" />
      <div class="panel-inner">
        <div class="logo">
          <TenonLogo :size="40" />
          <span>TenonAdmin</span>
        </div>
        <div class="bar" :style="{ background: app.accent }" />
        <h1 class="headline">企业级<span :style="{ color: app.accent }">权限</span>管理后台</h1>
        <p class="sub">{{ t('login.subtitle') }}</p>
        <ul class="points">
          <li v-for="p in ['RBAC 角色授权', '多机构数据范围', '多应用门户']" :key="p">
            <Icon icon="ph:check-circle-duotone" :width="18" :style="{ color: app.accent }" />{{ p }}
          </li>
        </ul>
      </div>
    </div>

    <!-- 右:登录卡 -->
    <div class="form-wrap">
      <div class="card">
        <h2 class="card-title">{{ t('login.title') }}</h2>
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
        <p class="hint">superAdmin / 首启控制台打印的密码</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.login {
  height: 100vh;
  display: grid;
  grid-template-columns: 1.1fr 1fr;
  background: var(--color-bg-body);
}
.panel {
  position: relative;
  overflow: hidden;
  background: var(--color-bg-container);
  border-right: 1px solid var(--color-border);
}
.glow {
  position: absolute;
  width: 320px;
  height: 320px;
  top: -80px;
  left: -60px;
  border-radius: 50%;
  background: var(--color-primary-light);
  filter: blur(80px);
  opacity: 0.7;
}
.panel-inner {
  position: relative;
  padding: 64px 56px;
  display: flex;
  flex-direction: column;
  height: 100%;
  justify-content: center;
}
.logo {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 22px;
  font-weight: 700;
  color: var(--color-text-primary);
}
.bar {
  width: 40px;
  height: 3px;
  border-radius: 2px;
  margin: 28px 0 20px;
}
.headline {
  font-size: 42px;
  line-height: 1.2;
  font-weight: 800;
  color: var(--color-text-primary);
  margin: 0;
}
.sub {
  color: var(--color-text-secondary);
  margin: 16px 0 28px;
}
.points {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.points li {
  display: flex;
  align-items: center;
  gap: 10px;
  color: var(--color-text-secondary);
}
.form-wrap {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px;
}
.card {
  width: 100%;
  max-width: 400px;
  padding: 40px 36px;
  background: var(--color-bg-container);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-2);
}
.card-title {
  font-size: var(--font-size-lg);
  font-weight: 600;
  color: var(--color-text-primary);
  margin: 0 0 24px;
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
  transition: transform var(--transition-fast);
}
.hero-btn:hover {
  transform: translateY(-2px);
}
.hero-btn:disabled {
  opacity: 0.7;
  cursor: not-allowed;
}
.hint {
  margin-top: 18px;
  text-align: center;
  font-size: var(--font-size-sm);
  color: var(--color-text-tertiary);
}
@media (max-width: 900px) {
  .login {
    grid-template-columns: 1fr;
  }
  .panel {
    display: none;
  }
}
</style>
