<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NCard, NForm, NFormItem, NInput, NButton, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import { personalApi, authApi, configApi } from '@/api'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'
import { resetRouter } from '@/router'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()
const router = useRouter()
const user = useUserStore()
const auth = useAuthStore()

const model = reactive({ oldPassword: '', newPassword: '', confirmPassword: '' })
const saving = ref(false)

// 有效密码策略:onMounted 拉后端当前生效值,精确跟随超管在「安全策略」的改动。
// 默认 = 后端默认策略,作 fallback(拉取失败仍可用;后端始终强制,弱口令以 passwordTooWeak 兜底)。
const policy = ref({ minLength: 8, requireUpper: true, requireLower: true, requireDigit: true, requireSpecial: false })
onMounted(async () => {
  try {
    policy.value = await configApi.passwordPolicy()
  } catch { /* 保留默认;后端仍强制 */ }
})

const checks = computed(() => {
  const p = model.newPassword
  return {
    minLength: p.length >= policy.value.minLength,
    upper: /[A-Z]/.test(p),
    lower: /[a-z]/.test(p),
    digit: /\d/.test(p),
    special: /[^A-Za-z0-9]/.test(p),
  }
})
// 强度:长度达标 + 字符种类数 → 弱(1)/中(2)/强(3);空则 0
const strength = computed(() => {
  const p = model.newPassword
  if (!p) return 0
  const c = checks.value
  const variety = [c.upper, c.lower, c.digit, c.special].filter(Boolean).length
  if (!c.minLength || variety <= 1) return 1
  if (variety === 2 || p.length < 12) return 2
  return 3
})
const strengthText = computed(() => ['', t('changePassword.strength.weak'), t('changePassword.strength.fair'), t('changePassword.strength.strong')][strength.value])
const strengthColor = computed(() => ['', '#e88080', '#e0a458', '#5aa86e'][strength.value])
// 规则清单按有效策略动态构建:恒显最小长度;大小写/数字仅在策略要求时作硬规则;
// 特殊字符——策略要求时作硬规则,否则作可选提示。
const ruleList = computed(() => {
  const pol = policy.value
  const c = checks.value
  const rows = [{ key: 'minLength', ok: c.minLength, text: t('changePassword.rules.minLength', { n: pol.minLength }) }]
  if (pol.requireUpper) rows.push({ key: 'upper', ok: c.upper, text: t('changePassword.rules.upper') })
  if (pol.requireLower) rows.push({ key: 'lower', ok: c.lower, text: t('changePassword.rules.lower') })
  if (pol.requireDigit) rows.push({ key: 'digit', ok: c.digit, text: t('changePassword.rules.digit') })
  rows.push({
    key: 'special',
    ok: c.special,
    text: pol.requireSpecial ? t('changePassword.rules.special') : t('changePassword.rules.specialOptional'),
  })
  return rows
})

async function submit() {
  if (!model.oldPassword || !model.newPassword) {
    message.warning(t('changePassword.required'))
    return
  }
  if (model.newPassword !== model.confirmPassword) {
    message.error(t('changePassword.mismatch'))
    return
  }
  saving.value = true
  try {
    await personalApi.updatePassword({ oldPassword: model.oldPassword, newPassword: model.newPassword })
    message.success(t('changePassword.changed'))
    // 改密后强制重新登录
    try {
      await authApi.logout()
    } catch { /* 尽力而为 */ }
    resetRouter()
    auth.reset()
    user.clear()
    router.replace('/login')
  } catch (e) {
    message.error(translateError(e))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <n-card :bordered="true" :title="t('changePassword.title')" style="max-width: 460px">
    <n-form :model="model" label-placement="top">
      <n-form-item :label="t('changePassword.oldPassword')">
        <n-input v-model:value="model.oldPassword" type="password" show-password-on="click" />
      </n-form-item>
      <n-form-item :label="t('changePassword.newPassword')">
        <n-input v-model:value="model.newPassword" type="password" show-password-on="click" />
      </n-form-item>
      <div v-if="model.newPassword" class="pw-meter">
        <div class="pw-bar">
          <span v-for="i in 3" :key="i" :style="{ background: i <= strength ? strengthColor : 'var(--color-border)' }" />
        </div>
        <div class="pw-level" :style="{ color: strengthColor }">
          {{ t('changePassword.strength.label') }}:{{ strengthText }}
        </div>
        <ul class="pw-rules">
          <li v-for="r in ruleList" :key="r.key" :class="{ ok: r.ok }">
            <AppIcon :icon="r.ok ? 'ph:check-circle-fill' : 'ph:circle'" :size="14" />{{ r.text }}
          </li>
        </ul>
      </div>
      <n-form-item :label="t('changePassword.confirmPassword')">
        <n-input v-model:value="model.confirmPassword" type="password" show-password-on="click" @keyup.enter="submit" />
      </n-form-item>
      <n-button
        type="primary"
        block
        :loading="saving"
        :disabled="!model.oldPassword || !model.newPassword || !model.confirmPassword"
        @click="submit"
      >{{ t('common.submit') }}</n-button>
    </n-form>
  </n-card>
</template>

<style scoped>
.pw-meter {
  margin: -8px 0 14px;
}
.pw-bar {
  display: flex;
  gap: 6px;
}
.pw-bar span {
  flex: 1;
  height: 4px;
  border-radius: 2px;
  transition: background var(--transition-fast);
}
.pw-level {
  margin-top: 6px;
  font-size: 12px;
}
.pw-rules {
  margin: 8px 0 0;
  padding: 0;
  list-style: none;
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 2px 12px;
}
.pw-rules li {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  color: var(--color-text-tertiary);
}
.pw-rules li.ok {
  color: #5aa86e;
}
</style>
